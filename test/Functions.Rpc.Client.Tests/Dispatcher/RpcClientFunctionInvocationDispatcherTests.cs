// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientFunctionInvocationDispatcherTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly List<WorkerChannel> _channels = [];
    private readonly InitializedChannelsSignal _initializedChannelsSignal = new();
    private readonly Channel<(long Version, Task<long> Wait)> _initializedChannelsWaits = Channel.CreateUnbounded<(long, Task<long>)>();
    private readonly Mock<IWorkerChannelRegistry> _registry = new();

    public RpcClientFunctionInvocationDispatcherTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() =>
            {
                lock (_channels)
                {
                    return [.. _channels];
                }
            });
        _registry.SetupGet(registry => registry.InitializedChannelsVersion)
            .Returns(() => _initializedChannelsSignal.Version);
        _registry.Setup(registry => registry.WaitForInitializedChannelsChangeAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .Returns((long version, CancellationToken token) =>
            {
                Task<long> wait = _initializedChannelsSignal.WaitForChangeAsync(version, token);
                _initializedChannelsWaits.Writer.TryWrite((version, wait));
                return wait;
            });
        _registry.Setup(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()))
            .Returns((CancellationToken cancellationToken) => WaitForChannelAsync(cancellationToken));
        _registry.Setup(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
    }

    [Fact]
    public async Task InitializeAsync_ConfiguresInitializedChannels()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();

        Task initialization = dispatcher.InitializeAsync([function]);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await initialization.WaitAsync(TestTimeout);
        Assert.True(worker.Channel.InvocationBuffersInitialization.IsCompletedSuccessfully);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());

        Assert.Equal(FunctionInvocationDispatcherState.Initialized, dispatcher.State);
        Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
        Assert.True(worker.Channel.FunctionInputBuffers.ContainsKey(function.GetFunctionId()));
    }

    [Fact]
    public async Task InitializeAsync_SetsUpLaterLinkedWorkersOnce()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        await using ClientWorkerChannelTestHarness third = await ClientWorkerChannelTestHarness.CreateAsync("c-worker");
        _channels.Add(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, first);
        var firstBuffer = first.Channel.FunctionInputBuffers[function.GetFunctionId()];

        LinkChannel(second.Channel);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await second.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);
        var secondBuffer = second.Channel.FunctionInputBuffers[function.GetFunctionId()];
        _initializedChannelsSignal.Signal();
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);
        LinkChannel(third.Channel);
        await third.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await third.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);

        Assert.Same(firstBuffer, first.Channel.FunctionInputBuffers[function.GetFunctionId()]);
        Assert.Same(secondBuffer, second.Channel.FunctionInputBuffers[function.GetFunctionId()]);
        Assert.False(first.Transport.Requests.TryRead(out _));
        Assert.False(second.Transport.Requests.TryRead(out _));
        foreach (ClientWorkerChannelTestHarness worker in new[] { first, second, third })
        {
            ScriptInvocationContext invocation = CreateInvocation(function);
            await dispatcher.InvokeAsync(invocation);
            StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
            await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
            Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task InitializeAsync_SetsUpWorkerLinkedWhileInitializing()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness racing = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(first.Channel);
        bool linked = false;
        _registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
        {
            WorkerChannel[] snapshot = [.. _channels];
            if (!linked)
            {
                linked = true;
                LinkChannel(racing.Channel);
            }

            return snapshot;
        });
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();

        await InitializeDispatcherAsync(dispatcher, function, first);
        StreamingMessage request = await racing.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);

        Assert.Equal(function.GetFunctionId(), request.FunctionLoadRequest.FunctionId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RemovedChannels_SnapshotsAreNotRetainedByLaterLinkWatcher(bool linkedDuringStartup)
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        WeakReference snapshot = await CreateAndRemoveChannelAsync(dispatcher, linkedDuringStartup);
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);
        // Moq records snapshot return values as well as calls.
        _registry.Invocations.Clear();

        for (int attempt = 0; attempt < 10 && snapshot.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            await Task.Delay(20);
        }

        Assert.False(snapshot.IsAlive);
        GC.KeepAlive(dispatcher);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> CreateAndRemoveChannelAsync(
        RpcClientFunctionInvocationDispatcher dispatcher, bool linkedDuringStartup)
    {
        WeakReference snapshot = new(null);
        _registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
        {
            lock (_channels)
            {
                WorkerChannel[] channels = [.. _channels];
                if (channels.Length > 0)
                {
                    snapshot.Target = channels;
                }

                return channels;
            }
        });
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("removed");
        FunctionMetadata function = CreateFunction();
        if (linkedDuringStartup)
        {
            _channels.Add(worker.Channel);
            await InitializeDispatcherAsync(dispatcher, function, worker);
        }
        else
        {
            await dispatcher.InitializeAsync([function]);
            LinkChannel(worker.Channel);
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        }

        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);
        lock (_channels)
        {
            _channels.Clear();
        }

        _initializedChannelsSignal.Signal();
        return snapshot;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreShutdownOrDispose_StopsWaitingForLaterLinkedWorkers(bool dispose)
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([CreateFunction()]);
        Task<long> wait = await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);

        if (dispose)
        {
            dispatcher.Dispose();
        }
        else
        {
            dispatcher.PreShutdown();
        }

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreShutdownOrDispose_WaitsForActiveSetupBeforeHandoff(bool dispose)
    {
        await using ClientWorkerChannelTestHarness later = await ClientWorkerChannelTestHarness.CreateAsync("later");
        using ManualResetEventSlim releaseSetup = new();
        TaskCompletionSource setupStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<ILogger<RpcClientFunctionInvocationDispatcher>> logger = CreateLogger();
        logger.Setup(value => value.Log(
                LogLevel.Information,
                It.Is<EventId>(eventId => string.Equals(eventId.Name, "SettingUpLaterLinkedChannel", StringComparison.Ordinal)),
                It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception, string>>()))
            .Callback(() =>
            {
                setupStarted.TrySetResult();
                if (!releaseSetup.Wait(TestTimeout))
                {
                    throw new TimeoutException("Channel setup was not released.");
                }
            });
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(logger: logger.Object);
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread shutdownThread = new(() =>
        {
            try
            {
                if (dispose)
                {
                    dispatcher.Dispose();
                }
                else
                {
                    dispatcher.PreShutdown();
                }

                stopped.TrySetResult();
            }
            catch (Exception exception)
            {
                stopped.TrySetException(exception);
            }
        }) { IsBackground = true };

        try
        {
            LinkChannel(later.Channel);
            await setupStarted.Task.WaitAsync(TestTimeout);
            shutdownThread.Start();
            Assert.True(SpinWait.SpinUntil(() => stopped.Task.IsCompleted ||
                shutdownThread.ThreadState.HasFlag(ThreadState.WaitSleepJoin), TestTimeout));
            Assert.False(stopped.Task.IsCompleted);
        }
        finally
        {
            releaseSetup.Set();
            if (!shutdownThread.ThreadState.HasFlag(ThreadState.Unstarted))
            {
                await stopped.Task.WaitAsync(TestTimeout);
                Assert.True(shutdownThread.Join(TestTimeout));
            }
        }

        await later.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await later.SendFunctionLoadResponseAsync(function.GetFunctionId());
        var oldBuffer = later.Channel.FunctionInputBuffers[function.GetFunctionId()];
        using RpcClientFunctionInvocationDispatcher nextDispatcher = CreateDispatcher();
        await InitializeDispatcherAsync(nextDispatcher, function, later);
        var nextBuffer = later.Channel.FunctionInputBuffers[function.GetFunctionId()];
        Assert.NotSame(oldBuffer, nextBuffer);
        _initializedChannelsSignal.Signal();
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);
        Assert.Same(nextBuffer, later.Channel.FunctionInputBuffers[function.GetFunctionId()]);
        ScriptInvocationContext invocation = CreateInvocation(function);
        await nextDispatcher.InvokeAsync(invocation);
        StreamingMessage request = await later.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await later.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task PreShutdown_RacingLaterLinkLeavesSetupToNextDispatcher()
    {
        await using ClientWorkerChannelTestHarness later = await ClientWorkerChannelTestHarness.CreateAsync("later");
        Mock<ILogger<RpcClientFunctionInvocationDispatcher>> logger = CreateLogger();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(logger: logger.Object);
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        _registry.Setup(registry => registry.GetInitializedChannels()).Returns(() =>
        {
            dispatcher.PreShutdown();
            return [.. _channels];
        });

        LinkChannel(later.Channel);
        await WaitForRegistryWaitAsync(_initializedChannelsSignal.Version);

        Assert.Empty(later.Channel.FunctionInputBuffers);
        Assert.False(later.Transport.Requests.TryRead(out _));
        Assert.DoesNotContain(logger.Invocations, invocation =>
            string.Equals(invocation.Method.Name, nameof(ILogger.Log), StringComparison.Ordinal) &&
            invocation.Arguments[0] is LogLevel.Warning or LogLevel.Error);
        using RpcClientFunctionInvocationDispatcher nextDispatcher = CreateDispatcher();
        await InitializeDispatcherAsync(nextDispatcher, function, later);
        Assert.Single(later.Channel.FunctionInputBuffers);
    }

    [Fact]
    public async Task InvokeAsync_CompletesSuccessfulInvocation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        await InitializeDispatcherAsync(dispatcher, function, worker);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);

        ScriptInvocationResult result = await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task InvokeAsync_SurfacesFunctionLoadFailure()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        Task initialization = dispatcher.InitializeAsync([function]);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId(), succeeded: false);
        await initialization.WaitAsync(TestTimeout);
        await dispatcher.InvokeAsync(invocation);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.Contains("function load failed", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesInvocationCancellation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        using CancellationTokenSource cancellationSource = new();
        ScriptInvocationContext invocation = CreateInvocation(function, cancellationSource.Token);

        await InitializeDispatcherAsync(dispatcher, function, worker);
        cancellationSource.Cancel();
        await dispatcher.InvokeAsync(invocation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_WaitsForFirstInitializedChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);

        await dispatcher.InitializeAsync([function]);
        Task invoke = dispatcher.InvokeAsync(invocation);
        Assert.False(invoke.IsCompleted);

        _channels.Add(worker.Channel);
        Task setup = dispatcher.SetupChannelAsync(worker.Channel);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        Assert.True(setup.IsCompletedSuccessfully);
        await setup.WaitAsync(TestTimeout);
        await invoke.WaitAsync(TestTimeout);

        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_CancellationWhileWaitingIsCallerVisible()
    {
        using CancellationTokenSource cancellationSource = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function, cancellationSource.Token);
        await dispatcher.InitializeAsync([function]);

        Task invoke = dispatcher.InvokeAsync(invocation);
        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invoke.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_NoInitializedChannelTimesOut()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext invocation = CreateInvocation(function);
        await dispatcher.InitializeAsync([function]);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => dispatcher.InvokeAsync(invocation).WaitAsync(TestTimeout));

        Assert.Contains("No client-backed worker channel became ready", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_SelectsReadyChannelsRoundRobin()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(second.Channel);
        _channels.Add(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, first, second);

        await dispatcher.InvokeAsync(CreateInvocation(function));
        await dispatcher.InvokeAsync(CreateInvocation(function));

        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_ReadyChannelUsesSingleRegistrySnapshot()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, worker);
        _registry.Invocations.Clear();

        await dispatcher.InvokeAsync(CreateInvocation(function));

        _registry.Verify(registry => registry.GetInitializedChannels(), Times.Once);
        _registry.Verify(registry => registry.WaitForFirstInitializedAsync(It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(registry => registry.UnlinkAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SetupChannelAsync_CompletesWhenInvocationBuffersAreInitialized()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, first);

        _channels.Add(second.Channel);
        Task setup = dispatcher.SetupChannelAsync(second.Channel);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await setup.WaitAsync(TestTimeout);
        Assert.True(second.Channel.InvocationBuffersInitialization.IsCompletedSuccessfully);

        await second.SendFunctionLoadResponseAsync(function.GetFunctionId());
    }

    [Fact]
    public async Task SetupChannelAsync_AfterPreShutdownDoesNotConfigureChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        dispatcher.PreShutdown();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.SetupChannelAsync(worker.Channel));

        Assert.Contains("stopping", exception.Message, StringComparison.Ordinal);
        Assert.False(worker.Channel.InvocationBuffersInitialization.IsCompleted);
        Assert.Empty(worker.Channel.FunctionInputBuffers);
    }

    [Fact]
    public async Task InvokeAsync_ChannelFailureDoesNotPreventLaterDispatch()
    {
        await using ClientWorkerChannelTestHarness failed = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness healthy = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        _channels.Add(failed.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        ScriptInvocationContext failedInvocation = CreateInvocation(function);
        await InitializeDispatcherAsync(dispatcher, function, failed);
        await dispatcher.InvokeAsync(failedInvocation);
        await failed.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        failed.Transport.CompleteResponses(new InvalidOperationException("transport failed"));
        await Assert.ThrowsAnyAsync<Exception>(() => failedInvocation.ResultSource.Task.WaitAsync(TestTimeout));
        _channels.Clear();
        _channels.Add(healthy.Channel);
        Task setup = dispatcher.SetupChannelAsync(healthy.Channel);
        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await healthy.SendFunctionLoadResponseAsync(function.GetFunctionId());
        await setup.WaitAsync(TestTimeout);

        await dispatcher.InvokeAsync(CreateInvocation(function));

        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task GetWorkerStatusesAsync_NoInitializedChannelsReturnsEmpty()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();

        IDictionary<string, WorkerStatus> statuses = await dispatcher.GetWorkerStatusesAsync();

        Assert.Empty(statuses);
    }

    [Fact]
    public void Factory_ReturnsRegisteredDispatcher()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        RpcClientFunctionInvocationDispatcherFactory factory = new(dispatcher);

        IFunctionInvocationDispatcher first = factory.GetFunctionDispatcher();
        IFunctionInvocationDispatcher second = factory.GetFunctionDispatcher();

        Assert.Same(first, second);
        Assert.Same(dispatcher, first);
    }

    [Fact]
    public async Task Dispose_RejectsNewInvocationsWithoutDisposingChannel()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, worker);
        _registry.Invocations.Clear();

        dispatcher.Dispose();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => dispatcher.InvokeAsync(CreateInvocation(function)));

        Assert.Equal(typeof(RpcClientFunctionInvocationDispatcher).FullName, exception.ObjectName);
        Assert.Equal(FunctionInvocationDispatcherState.Disposed, dispatcher.State);
        Assert.Equal(0, worker.Transport.DisposeCount);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreShutdown_WithReadyChannelRejectsNewInvocations()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        _channels.Add(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await InitializeDispatcherAsync(dispatcher, function, worker);
        _registry.Invocations.Clear();

        dispatcher.PreShutdown();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(CreateInvocation(function)));

        Assert.Contains("stopping", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
        Assert.Equal(0, worker.Transport.DisposeCount);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreShutdown_RejectsNewInvocations()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        _registry.Invocations.Clear();

        dispatcher.PreShutdown();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(CreateInvocation(function)));
        Assert.Contains("stopping", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
        _registry.VerifyNoOtherCalls();
    }

    private RpcClientFunctionInvocationDispatcher CreateDispatcher(
        TimeSpan? channelWaitTimeout = null, ILogger<RpcClientFunctionInvocationDispatcher> logger = null)
        => new(
            _registry.Object,
            Options.Create(new ScriptJobHostOptions()),
            Options.Create(new ManagedDependencyOptions()),
            logger ?? NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            channelWaitTimeout ?? TimeSpan.FromSeconds(10));

    private static Mock<ILogger<RpcClientFunctionInvocationDispatcher>> CreateLogger()
    {
        Mock<ILogger<RpcClientFunctionInvocationDispatcher>> logger = new();
        logger.Setup(value => value.IsEnabled(It.IsAny<LogLevel>())).Returns(true);
        return logger;
    }

    private void LinkChannel(WorkerChannel channel)
    {
        lock (_channels)
        {
            _channels.Add(channel);
        }

        _initializedChannelsSignal.Signal();
    }

    private async Task<Task<long>> WaitForRegistryWaitAsync(long version)
    {
        while (true)
        {
            var observed = await _initializedChannelsWaits.Reader.ReadAsync().AsTask().WaitAsync(TestTimeout);
            if (observed.Version == version)
            {
                return observed.Wait;
            }
        }
    }

    private async Task<WorkerChannel> WaitForChannelAsync(CancellationToken cancellationToken)
    {
        while (_channels.Count == 0)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }

        return _channels[0];
    }

    private static async Task InitializeDispatcherAsync(
        RpcClientFunctionInvocationDispatcher dispatcher,
        FunctionMetadata function,
        params ClientWorkerChannelTestHarness[] workers)
    {
        Task initialization = dispatcher.InitializeAsync([function]);
        foreach (ClientWorkerChannelTestHarness worker in workers)
        {
            await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
            await worker.SendFunctionLoadResponseAsync(function.GetFunctionId());
        }

        await initialization.WaitAsync(TestTimeout);
    }

    private static FunctionMetadata CreateFunction()
    {
        FunctionMetadata function = new()
        {
            Language = "external",
            Name = "TestFunction",
        };
        return function;
    }

    private static ScriptInvocationContext CreateInvocation(FunctionMetadata function, CancellationToken cancellationToken = default)
        => new()
        {
            FunctionMetadata = function,
            ExecutionContext = new()
            {
                FunctionName = function.Name,
                InvocationId = Guid.NewGuid(),
            },
            BindingData = [],
            Inputs = [],
            ResultSource = new(TaskCreationOptions.RunContinuationsAsynchronously),
            CancellationToken = cancellationToken,
            AsyncExecutionContext = System.Threading.ExecutionContext.Capture(),
            Logger = NullLogger.Instance,
        };
}
