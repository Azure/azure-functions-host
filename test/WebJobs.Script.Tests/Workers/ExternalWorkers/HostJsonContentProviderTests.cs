// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

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
    }
}
