// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using RpcExceptionMessage = Microsoft.Azure.WebJobs.Script.Grpc.Messages.RpcException;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientWorkerChannelTests
{
    private const string WorkerId = "test-worker";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);

    private readonly Mock<IAppCapabilitiesStore> _appCapabilitiesStore = new();
    private readonly RpcClientWorkerChannelFactory _factory;
    private readonly IMetricsLogger _metricsLogger = Mock.Of<IMetricsLogger>();

    public RpcClientWorkerChannelTests()
    {
        _factory = CreateFactory();
    }

    [Fact]
    public async Task ManagedIdentityTokenRequest_BeforeStartStream_AcceptsUnsignedRequestAndForwardsRawResponse()
    {
        const string endpoint = "http://127.0.0.1:41741/msi/token?existing=value";
        const string runtimeSecret = "runtime-secret";
        byte[] responseBody = [0, 1, 2, 254, 255];
        Uri requestUri = null;
        string identityHeader = null;
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateFactory(
            new Dictionary<string, string>
            {
                [EnvironmentSettingNames.MsiEndpoint] = endpoint,
                [EnvironmentSettingNames.MsiSecret] = runtimeSecret
            },
            (request, _) =>
            {
                requestUri = request.RequestUri;
                identityHeader = Assert.Single(request.Headers.GetValues("Secret"));
                ByteArrayContent content = new(responseBody);
                content.Headers.ContentType = MediaTypeHeaderValue.Parse("application/problem+json; charset=utf-8");
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = content
                });
            }).Create(WorkerId, duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);
        ManagedIdentityTokenRequest tokenRequest = new()
        {
            Resource = "https://storage.azure.com/",
            ClientId = "client id",
            ApiVersion = "2017-09-01",
            RuntimeAccessKeySignature = ByteString.Empty
        };

        await duplexChannel.SendResponseAsync(new()
        {
            RequestId = "token-request",
            ManagedIdentityTokenRequest = tokenRequest
        });

        StreamingMessage response = await duplexChannel.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal("token-request", response.RequestId);
        Assert.Equal(StreamingMessage.ContentOneofCase.ManagedIdentityTokenResponse, response.ContentCase);
        Assert.Equal(StatusResult.Types.Status.Success, response.ManagedIdentityTokenResponse.Result.Status);
        Assert.Equal((int)HttpStatusCode.TooManyRequests, response.ManagedIdentityTokenResponse.StatusCode);
        Assert.Equal("application/problem+json; charset=utf-8", response.ManagedIdentityTokenResponse.ContentType);
        Assert.Equal(responseBody, response.ManagedIdentityTokenResponse.Body.ToByteArray());
        Assert.Equal(
            "http://127.0.0.1:41741/msi/token?existing=value&resource=https%3A%2F%2Fstorage.azure.com%2F&api-version=2017-09-01&clientid=client%20id",
            requestUri?.AbsoluteUri);
        Assert.Equal(runtimeSecret, identityHeader);
        Assert.False(start.IsCompleted);

        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        Assert.False(start.IsCompleted);
        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = CreateSuccessfulInitResponse() });
        await start.WaitAsync(TestTimeout);
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CompletesAfterHandshakeAndAppliesInitialization()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);

        StreamingMessage initRequest = await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        WorkerInitResponse initResponse = CreateSuccessfulInitResponse();
        initResponse.Capabilities.Add("TestCapability", "enabled");
        initResponse.AppCapabilities.Add("TestAppCapability", "enabled");
        initResponse.WorkerMetadata = new WorkerMetadata
        {
            RuntimeName = "node",
            RuntimeVersion = "24.0",
        };

        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = initResponse });
        await start.WaitAsync(TestTimeout);

        Assert.Equal(StreamingMessage.ContentOneofCase.WorkerInitRequest, initRequest.ContentCase);
        Assert.Equal("c:\\test", initRequest.WorkerInitRequest.FunctionAppDirectory);
        Assert.Equal("node", channel.WorkerConfig.Description.DefaultRuntimeName);
        Assert.Equal("24.0", channel.WorkerConfig.Description.DefaultRuntimeVersion);
        _appCapabilitiesStore.Verify(store => store.TrySetAll(
            It.Is<IEnumerable<KeyValuePair<string, string>>>(capabilities =>
                capabilities.Any(capability => string.Equals(capability.Key, "TestAppCapability", StringComparison.Ordinal) &&
                    string.Equals(capability.Value, "enabled", StringComparison.Ordinal)))), Times.Once);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_PropagatesWorkerInitializationFailure()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        WorkerInitResponse initResponse = new()
        {
            Result = new StatusResult
            {
                Status = StatusResult.Types.Status.Failure,
                Exception = new() { Message = "worker initialization failed" },
            },
        };

        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = initResponse });

        Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException exception =
            await Assert.ThrowsAsync<Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException>(() => start.WaitAsync(TestTimeout));
        Assert.Contains("worker initialization failed", exception.Message, StringComparison.Ordinal);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_StartStreamTimeout_PropagatesTimeout()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        channel.WorkerConfig.CountOptions.ProcessStartupTimeout = TimeSpan.FromMilliseconds(50);

        await Assert.ThrowsAsync<TimeoutException>(() => channel.StartAsync(CancellationToken.None).WaitAsync(TestTimeout));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_InitializationTimeout_PropagatesTimeout()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        channel.WorkerConfig.CountOptions.InitializationTimeout = TimeSpan.FromMilliseconds(50);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(duplexChannel);

        await Assert.ThrowsAsync<TimeoutException>(() => start.WaitAsync(TestTimeout));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CleanChannelCompletionBeforeInitialization_Throws()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);

        duplexChannel.CompleteResponses();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => start.WaitAsync(TestTimeout));
        Assert.Equal("The RPC channel closed unexpectedly before the worker initialized.", exception.Message);
        await duplexChannel.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(1, duplexChannel.DisposeCount);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_FaultedChannelBeforeInitialization_PropagatesFailure()
    {
        InvalidOperationException expected = new("channel failed");
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);

        duplexChannel.CompleteResponses(expected);

        InvalidOperationException actual = await Assert.ThrowsAsync<InvalidOperationException>(() => start.WaitAsync(TestTimeout));
        Assert.Same(expected, actual);
        await duplexChannel.DisposeStarted.WaitAsync(TestTimeout);
        Assert.Equal(1, duplexChannel.DisposeCount);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_ConcurrentAndRepeatedCallsShareInitialization()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);

        Task first = channel.StartAsync(CancellationToken.None);
        Task concurrent = channel.StartAsync(CancellationToken.None);
        Assert.Same(first, concurrent);

        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = CreateSuccessfulInitResponse() });
        await Task.WhenAll(first, concurrent).WaitAsync(TestTimeout);

        Task repeated = channel.StartAsync(CancellationToken.None);
        Assert.Same(first, repeated);
        Assert.True(repeated.IsCompletedSuccessfully);
        Assert.False(duplexChannel.Requests.TryRead(out _));

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_PreCanceledCallerDoesNotBeginInitialization()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        using CancellationTokenSource cancellationSource = new();
        cancellationSource.Cancel();

        Task canceledStart = channel.StartAsync(cancellationSource.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledStart);

        Task sharedStart = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = CreateSuccessfulInitResponse() });
        await sharedStart.WaitAsync(TestTimeout);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_CanceledCallerDoesNotCancelSharedInitialization()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        using CancellationTokenSource cancellationSource = new();

        Task canceledWait = channel.StartAsync(cancellationSource.Token);
        Task sharedStart = channel.StartAsync(CancellationToken.None);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledWait);
        Assert.False(sharedStart.IsCompleted);

        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = CreateSuccessfulInitResponse() });
        await sharedStart.WaitAsync(TestTimeout);

        await channel.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentWithStartDisposesChannelOnce()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new(blockDisposal: true);
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);

        Task firstDispose = channel.DisposeAsync().AsTask();
        Task concurrentDispose = channel.DisposeAsync().AsTask();
        await duplexChannel.DisposeStarted.WaitAsync(TestTimeout);

        Assert.Same(firstDispose, concurrentDispose);
        Assert.Equal(1, duplexChannel.DisposeCount);
        Assert.False(firstDispose.IsCompleted);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start.WaitAsync(TestTimeout));

        duplexChannel.AllowDispose();
        await Task.WhenAll(firstDispose, concurrentDispose).WaitAsync(TestTimeout);
        await channel.DisposeAsync();
        Assert.Equal(1, duplexChannel.DisposeCount);
    }

    [Fact]
    public async Task StartAsync_AfterDisposal_ThrowsObjectDisposedException()
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        await channel.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => channel.StartAsync(CancellationToken.None));
        Assert.Equal(1, duplexChannel.DisposeCount);
    }

    private RpcClientWorkerChannel CreateChannel(TestDuplexChannel<StreamingMessage> duplexChannel)
        => _factory.Create(WorkerId, duplexChannel);

    private RpcClientWorkerChannelFactory CreateFactory(
        IReadOnlyDictionary<string, string> configurationValues = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync = null)
    {
        Mock<IScriptHostManager> hostManager = new();
        hostManager.As<IServiceProvider>()
            .Setup(provider => provider.GetService(typeof(IOptions<ScriptJobHostOptions>)))
            .Returns(Options.Create(new ScriptJobHostOptions { RootScriptPath = "c:\\test" }));

        Mock<IOptionsMonitor<ScriptApplicationHostOptions>> applicationHostOptions = new();
        applicationHostOptions.SetupGet(options => options.CurrentValue)
            .Returns(new ScriptApplicationHostOptions { ScriptPath = "c:\\test" });

        _appCapabilitiesStore.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Returns(true);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues)
            .Build();
        Mock<IHttpClientFactory> httpClientFactory = new();
        HttpClient httpClient = new(new StubHttpMessageHandler(
            sendAsync ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)))));
        httpClientFactory.Setup(factory => factory.CreateClient(It.IsAny<string>())).Returns(httpClient);

        return new(
            new ScriptEventManager(),
            hostManager.Object,
            Mock.Of<IEnvironment>(),
            configuration,
            httpClientFactory.Object,
            NullLoggerFactory.Instance,
            applicationHostOptions.Object,
            Mock.Of<ISharedMemoryManager>(),
            Options.Create(new WorkerConcurrencyOptions()),
            Options.Create(new FunctionsHostingConfigOptions()),
            _appCapabilitiesStore.Object,
            Mock.Of<IHttpProxyService>(),
            _metricsLogger);
    }

    private static WorkerInitResponse CreateSuccessfulInitResponse()
        => new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
        };

    private static async Task<StreamingMessage> SendStartStreamAndReadInitRequestAsync(TestDuplexChannel<StreamingMessage> duplexChannel)
    {
        await duplexChannel.SendResponseAsync(new()
        {
            StartStream = new() { WorkerId = WorkerId },
        });

        return await duplexChannel.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => sendAsync(request, cancellationToken);
    }
}
