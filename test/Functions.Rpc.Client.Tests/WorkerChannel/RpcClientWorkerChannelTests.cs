// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
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
    private readonly Mock<IEnvironment> _environment = new();
    private readonly RpcClientWorkerChannelFactory _factory;
    private readonly IMetricsLogger _metricsLogger = Mock.Of<IMetricsLogger>();

    public RpcClientWorkerChannelTests()
    {
        _factory = CreateFactory();
    }

    private RpcClientWorkerChannelFactory CreateFactory()
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

        return new(
            new ScriptEventManager(),
            hostManager.Object,
            _environment.Object,
            NullLoggerFactory.Instance,
            applicationHostOptions.Object,
            Mock.Of<ISharedMemoryManager>(),
            Options.Create(new WorkerConcurrencyOptions()),
            Options.Create(new FunctionsHostingConfigOptions()),
            _appCapabilitiesStore.Object,
            Mock.Of<IHttpProxyService>(),
            _metricsLogger);
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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StartAsync_AdvertisesHostConfigurationAndCapturesOptionalDelivery(bool delivered, bool v2CompatibilityMode)
    {
        _environment.Setup(environment => environment.GetEnvironmentVariable(EnvironmentSettingNames.FunctionsV2CompatibilityModeKey))
            .Returns(v2CompatibilityMode ? "true" : "false");
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        await using RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);
        StreamingMessage request = await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        WorkerInitResponse response = CreateSuccessfulInitResponse();
        if (delivered)
        {
            response.HostConfiguration = new()
            {
                Status = HostConfiguration.Types.Status.Available,
                Content = ByteString.CopyFromUtf8("""{"version":"2.0"}"""),
            };
        }

        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = response });
        await start.WaitAsync(TestTimeout);

        Assert.Equal("1", request.WorkerInitRequest.Capabilities["HostConfiguration"]);
        Assert.Equal("10", request.WorkerInitRequest.Capabilities[RpcWorkerConstants.MultiStream]);
        Assert.Equal(v2CompatibilityMode, request.WorkerInitRequest.Capabilities.ContainsKey(RpcWorkerConstants.V2Compatable));
        if (v2CompatibilityMode)
        {
            Assert.Equal("true", request.WorkerInitRequest.Capabilities[RpcWorkerConstants.V2Compatable]);
        }

        if (delivered)
        {
            Assert.Equal(response.HostConfiguration, channel.HostConfiguration);
            Assert.NotSame(response.HostConfiguration, channel.HostConfiguration);
            response.HostConfiguration.Content = ByteString.CopyFromUtf8("changed");
            Assert.Equal("""{"version":"2.0"}""", channel.HostConfiguration.Content.ToStringUtf8());
        }
        else
        {
            Assert.Null(channel.HostConfiguration);
        }
    }

    [Theory]
    [InlineData("http", "10", true, 10)]
    [InlineData("HTTP", "16", true, 16)]
    [InlineData("http", "1", true, 1)]
    [InlineData("http", "1000", true, 1000)]
    [InlineData("background", "invalid", false, null)]
    [InlineData("background", null, false, null)]
    [InlineData("background", "0", false, null)]
    [InlineData("deployment:validation", null, false, null)]
    public async Task StartAsync_CapturesOnlyApplicableAssignmentCapabilities(string group, string supplied, bool isHttp, int? expected)
    {
        await using ClientWorkerChannelTestHarness harness = await ClientWorkerChannelTestHarness.CreateAsync(
            WorkerId, functionGroupName: group, maxHttpConcurrency: supplied);

        Assert.Equal(group, harness.Channel.FunctionGroupName);
        Assert.Equal(isHttp, harness.Channel.IsHttpFunctionGroup);
        Assert.Equal(expected, harness.Channel.MaxHttpConcurrency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("10 ")]
    [InlineData("+10")]
    [InlineData("invalid")]
    [InlineData("1.5")]
    [InlineData("-1")]
    [InlineData("0")]
    [InlineData("1001")]
    [InlineData("2147483648")]
    public async Task StartAsync_HttpWithoutValidExplicitConcurrencyFails(string supplied)
    {
        TestDuplexChannel<StreamingMessage> transport = new();
        await using RpcClientWorkerChannel channel = CreateChannel(transport);
        Assert.Null(channel.FunctionGroupName);
        Assert.Null(channel.MaxHttpConcurrency);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(transport);
        WorkerInitResponse response = CreateSuccessfulInitResponse();
        response.Capabilities[RpcClientWorkerChannel.FunctionGroupNameCapability] = "hTtP";
        if (supplied is not null)
        {
            response.Capabilities.Add(RpcClientWorkerChannel.MaxHttpConcurrencyCapability, supplied);
        }

        await transport.SendResponseAsync(new() { WorkerInitResponse = response });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => start.WaitAsync(TestTimeout));
        Assert.Contains("MaxHttpConcurrency", exception.Message, StringComparison.Ordinal);
        Assert.Null(channel.FunctionGroupName);
        Assert.Null(channel.MaxHttpConcurrency);
    }

    [Fact]
    public async Task StartAsync_CapturesAssignmentBeforeInitializationCompletes()
    {
        using ManualResetEventSlim releaseInitialization = new(false);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _appCapabilitiesStore.Setup(store => store.TrySetAll(It.IsAny<IEnumerable<KeyValuePair<string, string>>>()))
            .Callback(() =>
            {
                entered.TrySetResult();
                if (!releaseInitialization.Wait(TestTimeout))
                {
                    throw new TimeoutException("Initialization was not released.");
                }
            })
            .Returns(true);
        TestDuplexChannel<StreamingMessage> transport = new();
        await using RpcClientWorkerChannel channel = CreateChannel(transport);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(transport);
        WorkerInitResponse initial = CreateSuccessfulInitResponse();
        initial.Capabilities[RpcClientWorkerChannel.FunctionGroupNameCapability] = "http";
        initial.Capabilities[RpcClientWorkerChannel.MaxHttpConcurrencyCapability] = "10";
        initial.AppCapabilities.Add("TestAppCapability", "1");
        await transport.SendResponseAsync(new() { WorkerInitResponse = initial });

        try
        {
            await entered.Task.WaitAsync(TestTimeout);
            Assert.False(start.IsCompleted);
            Assert.Equal("http", channel.FunctionGroupName);
            Assert.Equal(10, channel.MaxHttpConcurrency);
        }
        finally
        {
            releaseInitialization.Set();
        }

        await start.WaitAsync(TestTimeout);
        Assert.Equal("http", channel.FunctionGroupName);
        Assert.Equal(10, channel.MaxHttpConcurrency);
    }

    [Fact]
    public async Task StartAsync_LaterCapabilitiesDoNotChangeCapturedAssignment()
    {
        await using ClientWorkerChannelTestHarness harness = await ClientWorkerChannelTestHarness.CreateAsync(
            WorkerId, functionGroupName: "http", maxHttpConcurrency: "10");
        Task reload = harness.Channel.SendFunctionEnvironmentReloadRequest();
        await harness.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionEnvironmentReloadRequest);
        FunctionEnvironmentReloadResponse response = new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
        };
        response.Capabilities.Add(RpcClientWorkerChannel.FunctionGroupNameCapability, "background");
        response.Capabilities.Add(RpcClientWorkerChannel.MaxHttpConcurrencyCapability, "50");
        await harness.Transport.SendResponseAsync(new() { FunctionEnvironmentReloadResponse = response });
        await reload.WaitAsync(TestTimeout);

        Assert.Equal("http", harness.Channel.FunctionGroupName);
        Assert.True(harness.Channel.IsHttpFunctionGroup);
        Assert.Equal(10, harness.Channel.MaxHttpConcurrency);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task StartAsync_WithoutAssignedGroupFails(string group)
    {
        TestDuplexChannel<StreamingMessage> transport = new();
        await using RpcClientWorkerChannel channel = CreateChannel(transport);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(transport);
        WorkerInitResponse response = CreateSuccessfulInitResponse();
        response.Capabilities.Clear();
        if (group is not null)
        {
            response.Capabilities[RpcClientWorkerChannel.FunctionGroupNameCapability] = group;
        }

        await transport.SendResponseAsync(new() { WorkerInitResponse = response });

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() => start.WaitAsync(TestTimeout));
        Assert.Contains("FunctionGroupName", exception.Message, StringComparison.Ordinal);
        Assert.Null(channel.FunctionGroupName);
        Assert.False(channel.IsReadyForInvocations);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FunctionLoadCallbackFailure_SignalsTerminationWithoutDisposingTransport(bool useCollection)
    {
        TestDuplexChannel<StreamingMessage> duplexChannel = new();
        await using RpcClientWorkerChannel channel = CreateChannel(duplexChannel);
        Task start = channel.StartAsync(CancellationToken.None);
        await SendStartStreamAndReadInitRequestAsync(duplexChannel);
        WorkerInitResponse initResponse = CreateSuccessfulInitResponse();
        if (useCollection)
        {
            initResponse.Capabilities.Add(RpcWorkerConstants.SupportsLoadResponseCollection, "1");
        }

        await duplexChannel.SendResponseAsync(new() { WorkerInitResponse = initResponse });
        await start.WaitAsync(TestTimeout);
        FunctionMetadata function = new() { Name = "TestFunction", Language = "external" };
        channel.SetupFunctionInvocationBuffers([function]);
        channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), functionTimeout: null);
        StreamingMessage request = await duplexChannel.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
        Assert.Equal(useCollection
            ? StreamingMessage.ContentOneofCase.FunctionLoadRequestCollection
            : StreamingMessage.ContentOneofCase.FunctionLoadRequest, request.ContentCase);
        Assert.True(channel.IsReadyForInvocations);

        FunctionLoadResponse response = new()
        {
            FunctionId = "unknown-function",
            Result = new() { Status = StatusResult.Types.Status.Success },
        };
        StreamingMessage message = new();
        if (useCollection)
        {
            message.FunctionLoadResponseCollection = new();
            message.FunctionLoadResponseCollection.FunctionLoadResponses.Add(response);
        }
        else
        {
            message.FunctionLoadResponse = response;
        }

        await duplexChannel.SendResponseAsync(message);
        Exception failure = await channel.FunctionLoadFailure.WaitAsync(TestTimeout);

        Assert.IsType<KeyNotFoundException>(failure);
        Assert.True(channel.IsTerminated);
        Assert.False(channel.IsReadyForInvocations);
        Assert.False(channel.Completion.IsCompleted);
        Assert.Equal(0, duplexChannel.DisposeCount);
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

    private static WorkerInitResponse CreateSuccessfulInitResponse()
        => new()
        {
            Result = new() { Status = StatusResult.Types.Status.Success },
            Capabilities = { [RpcClientWorkerChannel.FunctionGroupNameCapability] = "function:TestFunction" },
        };

    private static async Task<StreamingMessage> SendStartStreamAndReadInitRequestAsync(TestDuplexChannel<StreamingMessage> duplexChannel)
    {
        await duplexChannel.SendResponseAsync(new()
        {
            StartStream = new() { WorkerId = WorkerId },
        });

        return await duplexChannel.Requests.ReadAsync().AsTask().WaitAsync(TestTimeout);
    }
}
