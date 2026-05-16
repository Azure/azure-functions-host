// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Grpc.Net.Client.Balancer;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests.Workers.ExternalWorkers
{
    public class HostJsonContentProviderTests
    {
        [Fact]
        public void SetContent_SetsContentAndSignalsTcs()
        {
            var provider = new HostJsonContentProvider();
            string expected = "{\"version\":\"2.0\"}";

            provider.SetContent(expected);

            string result = provider.WaitForContent(TimeSpan.FromSeconds(1));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WaitForContent_TimesOut_WhenNoContentSet()
        {
            var provider = new HostJsonContentProvider();

            Assert.Throws<TimeoutException>(() => provider.WaitForContent(TimeSpan.FromMilliseconds(50)));
        }

        [Fact]
        public void Reset_ClearsCache_WhenClearCacheTrue()
        {
            var provider = new HostJsonContentProvider();
            provider.SetContent("{\"version\":\"2.0\"}");

            provider.Reset(clearCache: true);

            Assert.Throws<TimeoutException>(() => provider.WaitForContent(TimeSpan.FromMilliseconds(50)));
        }

        [Fact]
        public void Reset_PreservesCache_WhenClearCacheFalse()
        {
            var provider = new HostJsonContentProvider();
            string expected = "{\"version\":\"2.0\"}";
            provider.SetContent(expected);

            provider.Reset(clearCache: false);

            string result = provider.WaitForContent(TimeSpan.FromSeconds(1));
            Assert.Equal(expected, result);
        }

        [Fact]
        public void WaitForContent_WithConcurrentReset_DoesNotHang()
        {
            var provider = new HostJsonContentProvider();
            provider.SetContent("{\"version\":\"2.0\"}");

            // Reset with clearCache=false preserves content and re-creates _tcs.
            // WaitForContent must read the new _tcs atomically under the lock.
            provider.Reset(clearCache: false);

            string result = null;
            var task = Task.Run(() => result = provider.WaitForContent(TimeSpan.FromSeconds(2)));
            bool completed = task.Wait(TimeSpan.FromSeconds(5));

            Assert.True(completed, "WaitForContent should not hang after Reset(clearCache: false)");
            Assert.Equal("{\"version\":\"2.0\"}", result);
        }

        [Fact]
        public void WaitForContent_AfterResetClearCache_TimesOut()
        {
            var provider = new HostJsonContentProvider();
            provider.SetContent("{\"version\":\"2.0\"}");

            provider.Reset(clearCache: true);

            Assert.Throws<TimeoutException>(() => provider.WaitForContent(TimeSpan.FromMilliseconds(100)));
        }
    }

    public class ExternalWorkerOptionsTests
    {
        [Fact]
        public void IsEnabled_DefaultsFalse()
        {
            var options = new ExternalWorkerOptions();

            Assert.False(options.IsEnabled);
        }

        [Fact]
        public void GrpcEndpoint_DefaultsNull()
        {
            var options = new ExternalWorkerOptions();

            Assert.Null(options.GrpcEndpoint);
        }
    }

    public class ConnectedWorkerFunctionMetadataProviderTests
    {
        private readonly Mock<IConnectedWorkerChannelManager> _mockChannelManager = new();
        private readonly Mock<IWorkerRuntimeResolver> _mockRuntimeResolver = new();
        private readonly ConnectedWorkerFunctionMetadataProvider _provider;

        public ConnectedWorkerFunctionMetadataProviderTests()
        {
            _mockRuntimeResolver.Setup(r => r.GetWorkerRuntime(It.IsAny<string>()))
                .Returns("dotnet-isolated");

            _provider = new ConnectedWorkerFunctionMetadataProvider(
                _mockChannelManager.Object,
                NullLogger<ConnectedWorkerFunctionMetadataProvider>.Instance,
                _mockRuntimeResolver.Object);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_WaitsForChannel_ThenRetrievesMetadata()
        {
            var rawMetadata = new List<RawFunctionMetadata>
            {
                new RawFunctionMetadata
                {
                    Metadata = new FunctionMetadata { Name = "TestFunction" },
                    Bindings = new[] { "{\"type\":\"httpTrigger\",\"name\":\"req\",\"direction\":\"in\"}" },
                    UseDefaultMetadataIndexing = false
                }
            };

            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.GetFunctionMetadata()).ReturnsAsync(rawMetadata);

            _mockChannelManager.Setup(m => m.WaitForChannelAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockChannel.Object);

            FunctionMetadataResult result = await _provider.GetFunctionMetadataAsync(
                Array.Empty<RpcWorkerConfig>());

            Assert.False(result.UseDefaultMetadataIndexing);
            Assert.Single(result.Functions);
            Assert.Equal("TestFunction", result.Functions[0].Name);
        }

        [Fact]
        public async Task GetFunctionMetadataAsync_ReturnsDefaultIndexing_WhenWorkerOptsOut()
        {
            var rawMetadata = new List<RawFunctionMetadata>
            {
                new RawFunctionMetadata
                {
                    Metadata = new FunctionMetadata { Name = "TestFunction" },
                    Bindings = new[] { "{\"type\":\"httpTrigger\",\"name\":\"req\",\"direction\":\"in\"}" },
                    UseDefaultMetadataIndexing = true
                }
            };

            var mockChannel = new Mock<IRpcWorkerChannel>();
            mockChannel.Setup(c => c.GetFunctionMetadata()).ReturnsAsync(rawMetadata);

            _mockChannelManager.Setup(m => m.WaitForChannelAsync(It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(mockChannel.Object);

            FunctionMetadataResult result = await _provider.GetFunctionMetadataAsync(
                Array.Empty<RpcWorkerConfig>());

            Assert.True(result.UseDefaultMetadataIndexing);
            Assert.Empty(result.Functions);
        }
    }

    public class ExternalWorkerHostJsonConfigurationProviderTests
    {
        [Fact]
        public void Load_ParsesHostJsonIntoConfigKeys()
        {
            var contentProvider = new HostJsonContentProvider();
            contentProvider.SetContent("{\"version\":\"2.0\",\"logging\":{\"logLevel\":{\"default\":\"Information\"}}}");

            var provider = new ExternalWorkerHostJsonConfigurationProvider(
                contentProvider,
                NullLogger.Instance);

            provider.Load();

            Assert.True(provider.TryGet("AzureFunctionsJobHost:version", out string version));
            Assert.Equal("2.0", version);

            Assert.True(provider.TryGet("AzureFunctionsJobHost:logging:logLevel:default", out string logLevel));
            Assert.Equal("Information", logLevel);
        }
    }

    public class OutboundGrpcClientTests
    {
        private readonly ITestOutputHelper _output;

        public OutboundGrpcClientTests(ITestOutputHelper output)
        {
            _output = output;
        }

        // Mirrors the WorkerConnectionService.EstablishChannelAsync retry
        // pattern: keep creating fresh OutboundGrpcClient instances until
        // ConnectAsync succeeds. Measures the wall-clock time across the
        // for-loop boundary so a listener that comes up after the inner
        // 1 s DefaultReadyTimeout still surfaces realistic timing data.
        //
        // The connect attempt only requires TCP — once the listener accepts
        // the socket, GrpcChannel.ConnectAsync (the readiness probe inside
        // OutboundGrpcClient) reports Ready and OutboundGrpcClient.ConnectAsync
        // returns. The post-connect init handshake (WaitForInitAsync) is
        // NOT exercised here; that step lives in WorkerConnectionService
        // and would require a real gRPC server on the far side.
        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        [InlineData(1200)]
        [InlineData(1600)]
        public async Task ForLoopRetry_ConnectsThroughDefaultReadyTimeoutBoundary(int listenerDelayMs)
        {
            // Constants mirror the production WorkerConnectionService values.
            const int maxRetries = 50;
            TimeSpan retryDelay = TimeSpan.FromMilliseconds(25);

            int port = GetFreeLoopbackPort();

            using var eventManager = new ScriptEventManager();
            var factory = new OutboundGrpcClientFactory(eventManager, NullLoggerFactory.Instance);
            string workerId = $"test-worker-{Guid.NewGuid():N}";

            TcpListener listener = null;
            try
            {
                var listenerTask = Task.Run(async () =>
                {
                    if (listenerDelayMs > 0)
                    {
                        await Task.Delay(listenerDelayMs);
                    }
                    listener = StartListener(port);
                });

                using var overall = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                var sw = Stopwatch.StartNew();

                IOutboundGrpcClient client = factory.Create();
                int attempts = 0;
                Exception lastFailure = null;
                bool connected = false;

                for (int attempt = 1; attempt <= maxRetries && !connected && !overall.IsCancellationRequested; attempt++)
                {
                    attempts = attempt;
                    eventManager.AddGrpcChannels(workerId);
                    try
                    {
                        await client.ConnectAsync(workerId, new Uri($"http://127.0.0.1:{port}"), overall.Token);
                        connected = true;
                    }
                    catch (Exception ex)
                    {
                        lastFailure = ex;
                        eventManager.RemoveGrpcChannels(workerId);
                        await client.DisposeAsync();
                        client = factory.Create();

                        await Task.Delay(retryDelay, overall.Token);
                    }
                }

                sw.Stop();
                await listenerTask;
                await client.DisposeAsync();

                Assert.True(connected, $"Failed to connect after {attempts} attempts; last failure: {lastFailure?.Message}");

                _output.WriteLine(
                    $"Listener delay: {listenerDelayMs,5} ms | Attempts: {attempts,3} | Total elapsed: {sw.ElapsedMilliseconds,5} ms | Gap (elapsed - listener): {sw.ElapsedMilliseconds - listenerDelayMs,5} ms");

                // Upper bound is intentionally generous to accommodate Windows
                // TCP retransmit behaviour (which pushes loopback connects to
                // ~500 ms even when nothing is buffered). On Linux this stays
                // close to listenerDelayMs.
                Assert.True(
                    sw.ElapsedMilliseconds < listenerDelayMs + 1500,
                    $"Connect took {sw.ElapsedMilliseconds} ms — expected within {listenerDelayMs + 1500} ms of listener startup.");
            }
            finally
            {
                listener?.Stop();
            }
        }

        [Fact]
        public void CreateGrpcChannelOptions_UsesConstantBackoffPolicy()
        {
            var options = OutboundGrpcClient.CreateGrpcChannelOptions();

            Assert.NotNull(options.ServiceProvider);
            var factory = options.ServiceProvider.GetService(typeof(IBackoffPolicyFactory));
            Assert.IsType<ConstantBackoffPolicyFactory>(factory);
            var policy = ((IBackoffPolicyFactory)factory).Create();
            Assert.IsType<ConstantBackoffPolicy>(policy);
            Assert.Equal(OutboundGrpcClient.DefaultRetryInterval, policy.NextBackoff());
            Assert.Equal(OutboundGrpcClient.DefaultRetryInterval, policy.NextBackoff());
            Assert.Equal(OutboundGrpcClient.DefaultRetryInterval, policy.NextBackoff());
        }

        [Fact]
        public void ConstantBackoffPolicy_AlwaysReturnsSameInterval()
        {
            var policy = new ConstantBackoffPolicy(TimeSpan.FromMilliseconds(42));

            Assert.Equal(TimeSpan.FromMilliseconds(42), policy.NextBackoff());
            Assert.Equal(TimeSpan.FromMilliseconds(42), policy.NextBackoff());
            Assert.Equal(TimeSpan.FromMilliseconds(42), policy.NextBackoff());
        }

        [Fact]
        public void ConstantBackoffPolicy_RejectsNonPositiveInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantBackoffPolicy(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantBackoffPolicy(TimeSpan.FromMilliseconds(-1)));
        }

        [Fact]
        public void ConstantBackoffPolicyFactory_CreatesPoliciesWithConfiguredInterval()
        {
            var factory = new ConstantBackoffPolicyFactory(TimeSpan.FromMilliseconds(17));

            var policy1 = factory.Create();
            var policy2 = factory.Create();

            Assert.NotSame(policy1, policy2);
            Assert.Equal(TimeSpan.FromMilliseconds(17), policy1.NextBackoff());
            Assert.Equal(TimeSpan.FromMilliseconds(17), policy2.NextBackoff());
        }

        [Fact]
        public void ConstantBackoffPolicyFactory_RejectsNonPositiveInterval()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantBackoffPolicyFactory(TimeSpan.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ConstantBackoffPolicyFactory(TimeSpan.FromMilliseconds(-5)));
        }

        [Fact]
        public void CreateGrpcChannelOptions_UsesSocketsHttpHandlerWithKeepAliveSettings()
        {
            var options = OutboundGrpcClient.CreateGrpcChannelOptions();

            using var handler = Assert.IsType<SocketsHttpHandler>(options.HttpHandler);
            Assert.Equal(OutboundGrpcClient.DefaultKeepAlivePingDelay, handler.KeepAlivePingDelay);
            Assert.Equal(OutboundGrpcClient.DefaultKeepAlivePingTimeout, handler.KeepAlivePingTimeout);
            Assert.Equal(HttpKeepAlivePingPolicy.Always, handler.KeepAlivePingPolicy);
        }

        [Fact]
        public async Task DisposeAsync_CalledMultipleTimes_DoesNotThrow()
        {
            var eventManager = new Mock<IScriptEventManager>();
            var logger = new Mock<ILogger<OutboundGrpcClient>>();
            var client = new OutboundGrpcClient(eventManager.Object, logger.Object);

            await client.DisposeAsync();
            await client.DisposeAsync();
        }

        // Regression: previously the connect-path catch disposed the
        // internal CancellationTokenSource without nulling the field. A
        // subsequent DisposeAsync then read the disposed CTS via
        // Interlocked.Exchange and called CancelAsync on it, throwing
        // ObjectDisposedException. That escaping exception silently
        // aborted WorkerConnectionService's retry for-loop on attempt 1
        // — so the for-loop never iterated, which is the entire reason
        // it exists.
        [Fact]
        public async Task DisposeAsync_AfterFailedConnect_DoesNotThrow()
        {
            var client = CreateOutboundGrpcClient();
            int port = GetFreeLoopbackPort();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.ConnectAsync("test-worker", new Uri($"http://127.0.0.1:{port}"), cts.Token));

            var disposeException = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());

            Assert.Null(disposeException);
        }

        [Fact]
        public async Task DisposeAsync_CalledTwiceAfterFailedConnect_DoesNotThrow()
        {
            var client = CreateOutboundGrpcClient();
            int port = GetFreeLoopbackPort();

            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.ConnectAsync("test-worker", new Uri($"http://127.0.0.1:{port}"), cts.Token));

            await client.DisposeAsync();
            var secondDisposeException = await Record.ExceptionAsync(() => client.DisposeAsync().AsTask());

            Assert.Null(secondDisposeException);
        }

        private static OutboundGrpcClient CreateOutboundGrpcClient()
        {
            var eventManager = new Mock<IScriptEventManager>().Object;
            return new OutboundGrpcClient(eventManager, NullLogger<OutboundGrpcClient>.Instance);
        }

        private static int GetFreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            try
            {
                return ((IPEndPoint)probe.LocalEndpoint).Port;
            }
            finally
            {
                probe.Stop();
            }
        }

        private static TcpListener StartListener(int port)
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            _ = Task.Run(async () =>
            {
                try
                {
                    while (true)
                    {
                        var socket = await listener.AcceptSocketAsync();
                        _ = socket;
                    }
                }
                catch (ObjectDisposedException)
                {
                }
                catch (SocketException)
                {
                }
            });
            return listener;
        }
    }
}
