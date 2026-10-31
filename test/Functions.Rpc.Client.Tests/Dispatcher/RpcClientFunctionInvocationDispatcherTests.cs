// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Azure.Functions.Rpc.Client.Tests;

public sealed class RpcClientFunctionInvocationDispatcherTests
{
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(10);
    private readonly List<WorkerChannel> _initializedChannels = [];
    private readonly List<WorkerChannel> _readyChannels = [];
    private readonly TaskCompletionSource<IReadOnlyList<WorkerChannel>> _readySignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly Mock<IWorkerChannelRegistry> _registry = new();

    public RpcClientFunctionInvocationDispatcherTests()
    {
        _registry.Setup(registry => registry.GetInitializedChannels())
            .Returns(() => [.. _initializedChannels]);
        _registry.Setup(registry => registry.GetReadyChannelsAsync())
            .Returns(() => _readyChannels.Count == 0
                ? new ValueTask<IReadOnlyList<WorkerChannel>>(_readySignal.Task)
                : new ValueTask<IReadOnlyList<WorkerChannel>>(_readyChannels.ToArray()));
    }

    [Fact]
    public async Task InitializeAsync_NonEmptyFunctionsOnlyUpdatesLocalState()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();

        await dispatcher.InitializeAsync([CreateFunction()]);

        Assert.Equal(FunctionInvocationDispatcherState.Initialized, dispatcher.State);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InitializeAsync_EmptyFunctionsLeaveDefaultState()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();

        await dispatcher.InitializeAsync([]);

        Assert.Equal(FunctionInvocationDispatcherState.Default, dispatcher.State);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task InvokeAsync_ReadySnapshotUsesSynchronousFastPath()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));

        Assert.True(invocation.IsCompletedSuccessfully);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_WaitsForReadySnapshot()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));
        Assert.False(invocation.IsCompleted);

        PublishReady(worker.Channel);
        await invocation.WaitAsync(TestTimeout);

        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_CompletesSuccessfulInvocation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);

        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);

        Assert.NotNull(await invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_SurfacesFunctionLoadFailure()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function, succeeded: false);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);

        await dispatcher.InvokeAsync(invocation);

        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
        Assert.Contains("function load failed", exception.ToString(), StringComparison.Ordinal);
        Assert.False(worker.Channel.FunctionLoadFailure.IsCompleted);
        Assert.False(worker.Channel.IsTerminated);
        Assert.True(worker.Channel.IsReadyForInvocations);
    }

    [Fact]
    public async Task InvokeAsync_ReadyChannelPropagatesInvocationCancellation()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync("worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        using CancellationTokenSource cancellationSource = new();
        ScriptInvocationContext invocation = CreateInvocation(function, cancellationSource.Token);
        cancellationSource.Cancel();

        await dispatcher.InvokeAsync(invocation);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => invocation.ResultSource.Task.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_SelectsReadyChannelsRoundRobin()
    {
        await using ClientWorkerChannelTestHarness first = await ClientWorkerChannelTestHarness.CreateAsync("a-worker");
        await using ClientWorkerChannelTestHarness second = await ClientWorkerChannelTestHarness.CreateAsync("b-worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(first, function);
        await PrepareReadyChannelAsync(second, function);
        PublishReady(second.Channel, first.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        await dispatcher.InvokeAsync(CreateInvocation(function));
        await dispatcher.InvokeAsync(CreateInvocation(function));

        await first.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await second.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_StaleSignaledSnapshotRequestsAnotherSnapshot()
    {
        await using ClientWorkerChannelTestHarness stale = await ClientWorkerChannelTestHarness.CreateAsync("stale");
        await using ClientWorkerChannelTestHarness ready = await ClientWorkerChannelTestHarness.CreateAsync("ready");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(ready, function);
        TaskCompletionSource<IReadOnlyList<WorkerChannel>> firstSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<IReadOnlyList<WorkerChannel>> secondSignal =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.SetupSequence(registry => registry.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(firstSignal.Task))
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(secondSignal.Task));
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));
        firstSignal.SetResult([stale.Channel]);
        secondSignal.SetResult([ready.Channel]);
        await invocation.WaitAsync(TestTimeout);

        await ready.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_StaleSnapshotExcludesCompletedTransport(bool includesHealthyChannel)
    {
        await using ClientWorkerChannelTestHarness stopped = await ClientWorkerChannelTestHarness.CreateAsync("stopped");
        await using ClientWorkerChannelTestHarness healthy = await ClientWorkerChannelTestHarness.CreateAsync("healthy");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(stopped, function);
        await PrepareReadyChannelAsync(healthy, function);
        IReadOnlyList<WorkerChannel> snapshot = includesHealthyChannel ? [stopped.Channel, healthy.Channel] : [stopped.Channel];
        stopped.Transport.CompleteResponses();
        await stopped.Channel.Completion.WaitAsync(TestTimeout);
        _registry.SetupSequence(registry => registry.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(snapshot))
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(_readySignal.Task));
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));
        if (!includesHealthyChannel)
        {
            Assert.False(invocation.IsCompleted);
            _readySignal.SetResult([healthy.Channel]);
        }

        await invocation.WaitAsync(TestTimeout);
        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Exactly(includesHealthyChannel ? 1 : 2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_CompletedSnapshotRetryObservesCancellation(bool stopDispatcher)
    {
        await using ClientWorkerChannelTestHarness stale = await ClientWorkerChannelTestHarness.CreateAsync("stale");
        await using ClientWorkerChannelTestHarness ready = await ClientWorkerChannelTestHarness.CreateAsync("ready");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(ready, function);
        using CancellationTokenSource cancellation = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        Action cancel = stopDispatcher ? dispatcher.PreShutdown : cancellation.Cancel;
        TaskCompletionSource<IReadOnlyList<WorkerChannel>> signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _registry.SetupSequence(registry => registry.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(signal.Task))
            .Returns(() =>
            {
                cancel();
                return new ValueTask<IReadOnlyList<WorkerChannel>>([ready.Channel]);
            });

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));
        signal.SetResult([stale.Channel]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Exactly(2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvokeAsync_CancellationDuringWaitingPathSelectionDoesNotDispatch(bool stopDispatcher)
    {
        await using ClientWorkerChannelTestHarness ready = await ClientWorkerChannelTestHarness.CreateAsync("ready");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(ready, function);
        using CancellationTokenSource cancellation = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        Action cancel = stopDispatcher ? dispatcher.PreShutdown : cancellation.Cancel;
        Mock<IReadOnlyList<WorkerChannel>> snapshot = new(MockBehavior.Strict);
        snapshot.SetupGet(channels => channels.Count).Returns(1);
        snapshot.Setup(channels => channels[0]).Returns(() =>
        {
            cancel();
            return ready.Channel;
        });

        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function, cancellation.Token));
        _readySignal.SetResult(snapshot.Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_ChannelFailureDoesNotPreventLaterDispatch()
    {
        await using ClientWorkerChannelTestHarness failed = await ClientWorkerChannelTestHarness.CreateAsync("failed");
        await using ClientWorkerChannelTestHarness healthy = await ClientWorkerChannelTestHarness.CreateAsync("healthy");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(failed, function);
        await PrepareReadyChannelAsync(healthy, function);
        PublishReady(failed.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext failedInvocation = CreateInvocation(function);
        await dispatcher.InvokeAsync(failedInvocation);
        await failed.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);

        failed.Transport.CompleteResponses(new InvalidOperationException("transport failed"));
        await Assert.ThrowsAnyAsync<Exception>(
            () => failedInvocation.ResultSource.Task.WaitAsync(TestTimeout));
        _readyChannels.Clear();
        _initializedChannels.Clear();
        PublishReady(healthy.Channel);

        await dispatcher.InvokeAsync(CreateInvocation(function));

        await healthy.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
    }

    [Fact]
    public async Task InvokeAsync_NoReadyChannelTimesOut()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);

        TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(
            () => dispatcher.InvokeAsync(CreateInvocation(function)).WaitAsync(TestTimeout));

        Assert.Contains("No client-backed worker channel became ready", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvokeAsync_CompletedStaleSnapshotsRespectTimeout()
    {
        await using ClientWorkerChannelTestHarness stale = await ClientWorkerChannelTestHarness.CreateAsync("stale");
        IReadOnlyList<WorkerChannel> snapshot = [stale.Channel];
        using CancellationTokenSource stopReturningSnapshots = new();
        _registry.Setup(registry => registry.GetReadyChannelsAsync())
            .Returns(() => stopReturningSnapshots.IsCancellationRequested
                ? new ValueTask<IReadOnlyList<WorkerChannel>>(_readySignal.Task)
                : new ValueTask<IReadOnlyList<WorkerChannel>>(snapshot));
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher(TimeSpan.FromMilliseconds(50));
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        Task invocation = Task.Run(() => dispatcher.InvokeAsync(CreateInvocation(function)));

        try
        {
            TimeoutException exception = await Assert.ThrowsAsync<TimeoutException>(() => invocation.WaitAsync(TestTimeout));
            Assert.Contains("No client-backed worker channel became ready", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            // Stop returning completed snapshots so even a regression cannot leave the retry loop running.
            stopReturningSnapshots.Cancel();
            await Assert.ThrowsAsync<TimeoutException>(() => invocation.WaitAsync(TestTimeout));
        }
    }

    [Fact]
    public async Task InvokeAsync_CancellationWhileWaitingIsCallerVisible()
    {
        using CancellationTokenSource cancellationSource = new();
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function, cancellationSource.Token));

        cancellationSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task InvokeAsync_StopWhileWaitingCancelsWait()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        FunctionMetadata function = CreateFunction();
        await dispatcher.InitializeAsync([function]);
        Task invocation = dispatcher.InvokeAsync(CreateInvocation(function));

        dispatcher.PreShutdown();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invocation.WaitAsync(TestTimeout));
        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
    }

    [Fact]
    public async Task ShutdownAsync_DrainsInitializedChannelSnapshot()
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker",
            trustedScaleInMetadata: true);
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        PublishReady(worker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);
        await dispatcher.InvokeAsync(invocation);

        Task shutdown = dispatcher.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);

        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        await shutdown.WaitAsync(TestTimeout);

        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
        _registry.Verify(registry => registry.GetInitializedChannels(), Times.Once);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task ShutdownAsync_DrainsInitializedChannelsExcludedFromActiveReadiness(
        bool readinessPending,
        bool admissionDisabled)
    {
        await using ClientWorkerChannelTestHarness worker = await ClientWorkerChannelTestHarness.CreateAsync(
            "worker",
            trustedScaleInMetadata: true);
        await using ClientWorkerChannelTestHarness otherWorker = await ClientWorkerChannelTestHarness.CreateAsync("other-worker");
        FunctionMetadata function = CreateFunction();
        await PrepareReadyChannelAsync(worker, function);
        await PrepareReadyChannelAsync(otherWorker, function);
        PublishReady(worker.Channel);
        _initializedChannels.Add(otherWorker.Channel);
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([function]);
        ScriptInvocationContext invocation = CreateInvocation(function);
        await dispatcher.InvokeAsync(invocation);
        StreamingMessage request = await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.InvocationRequest);
        if (admissionDisabled)
        {
            worker.Channel.SetScaleInReady(false);
            Assert.False(worker.Channel.IsReadyForInvocations);
        }

        TaskCompletionSource<IReadOnlyList<WorkerChannel>> replacementReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!readinessPending)
        {
            replacementReady.SetResult([otherWorker.Channel]);
        }

        _registry.Setup(registry => registry.GetReadyChannelsAsync())
            .Returns(new ValueTask<IReadOnlyList<WorkerChannel>>(replacementReady.Task));

        Task shutdown = dispatcher.ShutdownAsync();
        Assert.False(shutdown.IsCompleted);
        await worker.SendInvocationResponseAsync(request.InvocationRequest.InvocationId);
        await invocation.ResultSource.Task.WaitAsync(TestTimeout);
        await shutdown.WaitAsync(TestTimeout);

        Assert.Equal(!readinessPending, replacementReady.Task.IsCompleted);
        Assert.Equal(0, worker.Transport.DisposeCount);
        Assert.Equal(0, otherWorker.Transport.DisposeCount);
        _registry.Verify(registry => registry.GetInitializedChannels(), Times.Once);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Once);
    }

    [Fact]
    public async Task ShutdownAsync_DoesNotWaitForFutureReadyChannel()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([CreateFunction()]);

        await dispatcher.ShutdownAsync().WaitAsync(TestTimeout);

        Assert.False(_readySignal.Task.IsCompleted);
        _registry.Verify(registry => registry.GetInitializedChannels(), Times.Once);
        _registry.Verify(registry => registry.GetReadyChannelsAsync(), Times.Never);
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
    public async Task InvokeAsync_BeforeInitializationIsRejectedWithoutRegistryAccess()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(CreateInvocation(CreateFunction())));

        Assert.Contains("has not been initialized", exception.Message, StringComparison.Ordinal);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Dispose_RejectsNewInvocationsWithoutRegistryAccess()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([CreateFunction()]);
        dispatcher.Dispose();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => dispatcher.InvokeAsync(CreateInvocation(CreateFunction())));

        Assert.Equal(typeof(RpcClientFunctionInvocationDispatcher).FullName, exception.ObjectName);
        Assert.Equal(FunctionInvocationDispatcherState.Disposed, dispatcher.State);
        _registry.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PreShutdown_RejectsNewInvocationsWithoutRegistryAccess()
    {
        using RpcClientFunctionInvocationDispatcher dispatcher = CreateDispatcher();
        await dispatcher.InitializeAsync([CreateFunction()]);
        dispatcher.PreShutdown();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => dispatcher.InvokeAsync(CreateInvocation(CreateFunction())));

        Assert.Contains("stopping", exception.Message, StringComparison.Ordinal);
        Assert.Equal(FunctionInvocationDispatcherState.Disposing, dispatcher.State);
        _registry.VerifyNoOtherCalls();
    }

    private RpcClientFunctionInvocationDispatcher CreateDispatcher(TimeSpan? channelWaitTimeout = null)
        => new(
            _registry.Object,
            NullLogger<RpcClientFunctionInvocationDispatcher>.Instance,
            channelWaitTimeout ?? TimeSpan.FromSeconds(10));

    private void PublishReady(params WorkerChannel[] channels)
    {
        _readyChannels.AddRange(channels);
        _initializedChannels.AddRange(channels);
        _readySignal.TrySetResult(_readyChannels.ToArray());
    }

    private static async Task PrepareReadyChannelAsync(
        ClientWorkerChannelTestHarness worker,
        FunctionMetadata function,
        bool succeeded = true)
    {
        worker.Channel.SetupFunctionInvocationBuffers([function]);
        worker.Channel.SendFunctionLoadRequests(new ManagedDependencyOptions(), functionTimeout: null);
        await worker.ReadRequestAsync(StreamingMessage.ContentOneofCase.FunctionLoadRequest);
        await worker.SendFunctionLoadResponseAsync(function.GetFunctionId(), succeeded);
    }

    private static FunctionMetadata CreateFunction()
        => new()
        {
            Language = "external",
            Name = "TestFunction",
        };

    private static ScriptInvocationContext CreateInvocation(
        FunctionMetadata function,
        CancellationToken cancellationToken = default)
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
