// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_AcceptsAssignmentBeforeStartStreamAndPreservesItAcrossAttachment(bool runtimeFirst)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> runtimeInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> runtimeOutbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerOutbound = Channel.CreateUnbounded<StreamingMessage>();
        Task<FunctionRpcRelayTerminalState>? runtimeTask = null;

        if (runtimeFirst)
        {
            runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
                CreateMessageReader(runtimeInbound.Reader), CreateMessageWriter(runtimeOutbound.Writer), timeout.Token);
        }

        Assert.Equal(0, manager.State.Revision);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);

        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInbound.Reader), CreateMessageWriter(workerOutbound.Writer), timeout.Token);
        WorkerPodState attached = manager.State;
        Assert.Equal(2, attached.Revision);
        Assert.True(attached.IsWorkerAttached);
        Assert.False(attached.IsWorkerReady);
        Assert.Null(attached.WorkerId);
        Assert.Equal(WorkerAssignmentState.Assigned, attached.AssignmentState);

        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(attached.Revision, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await workerInbound.Writer.WriteAsync(start, timeout.Token);
        WorkerPodState ready = Assert.IsType<WorkerPodState>((await poll).State);
        Assert.Equal(3, ready.Revision);
        Assert.True(ready.IsWorkerReady);
        Assert.Equal(start.StartStream.WorkerId, ready.WorkerId);
        Assert.Equal(WorkerAssignmentState.Ready, ready.AssignmentState);
        Assert.Equal(WorkerPodStatus.Running, manager.State.PodStatus);
        Assert.Equal(WorkerStatus.Starting, WorkerOriginState.FromState(ready).Status);
        Assert.Equal(WorkerLinkState.Unlinked, WorkerOriginState.FromState(ready).Link.State);

        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(CreateWorkerAssignment()));
        Assert.Equal(3, manager.State.Revision);
        Assert.False(workerOutbound.Reader.TryRead(out _));

        runtimeTask ??= relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            CreateMessageReader(runtimeInbound.Reader), CreateMessageWriter(runtimeOutbound.Writer), timeout.Token);
        Assert.Equal(start, await runtimeOutbound.Reader.ReadAsync(timeout.Token));

        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtimeInbound.Writer.WriteAsync(init, timeout.Token);
        Assert.Same(init, await workerOutbound.Reader.ReadAsync(timeout.Token));
        Assert.False(workerOutbound.Reader.TryRead(out _));
        Assert.Equal(3, manager.State.Revision);

        Task<WorkerStatePollResult> drainingPoll = manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);
        await runtimeInbound.Writer.WriteAsync(new() { WorkerDraining = new() }, timeout.Token);
        WorkerPodState draining = Assert.IsType<WorkerPodState>((await drainingPoll).State);
        Assert.Equal(WorkerPodStatus.Draining, draining.PodStatus);
        Assert.Equal(WorkerStatus.Draining, WorkerOriginState.FromState(draining).Status);
        Assert.False(workerOutbound.Reader.TryRead(out _));

        await relay.StopAsync(timeout.Token);
        await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" \t")]
    public async Task Relay_InvalidFirstWorkerMessageFaultsWithoutReadinessOrForwarding(string? workerId)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> outbound = Channel.CreateUnbounded<StreamingMessage>();
        StreamingMessage first = workerId is null
            ? CreateMessage("not-start-stream")
            : new() { StartStream = new() { WorkerId = workerId } };

        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(), CreateMessageWriter(outbound.Writer), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(first), new TestServerStreamWriter(), timeout.Token);
        FunctionRpcRelayTerminalState[] terminalStates = await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);

        Assert.All(terminalStates, state =>
        {
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, state.Reason);
            Assert.Equal(FunctionRpcRelaySide.Worker, state.Side);
            Assert.IsType<InvalidDataException>(state.Exception);
        });
        Assert.False(outbound.Reader.TryRead(out _));
        Assert.False(manager.State.IsWorkerAttached);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Null(manager.State.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.Equal(2, manager.State.Revision);
    }

    [Fact]
    public async Task Relay_DuplicateWorkerDoesNotChangeReadiness()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(CreateStartStream()), new TestServerStreamWriter(), timeout.Token);
        WorkerPodState ready = manager.State;
        Assert.True(ready.IsWorkerReady);

        FunctionRpcRelayAttachmentException exception = await Assert.ThrowsAsync<FunctionRpcRelayAttachmentException>(() =>
            relay.AttachAsync(FunctionRpcRelaySide.Worker,
                new SingleMessageThenBlockStreamReader(CreateMessage("invalid-duplicate")), new TestServerStreamWriter(), timeout.Token));

        Assert.Equal(FunctionRpcRelayAttachmentFailure.Duplicate, exception.Failure);
        Assert.Same(ready, manager.State);
        await relay.StopAsync(timeout.Token);
        await workerTask.WaitAsync(timeout.Token);
    }

    [Theory]
    [InlineData("worker-close", nameof(FunctionRpcRelayTerminationReason.PeerClosed), true, false)]
    [InlineData("worker-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, false)]
    [InlineData("worker-write-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, false)]
    [InlineData("worker-cancel", nameof(FunctionRpcRelayTerminationReason.Canceled), true, false)]
    [InlineData("shutdown", nameof(FunctionRpcRelayTerminationReason.Shutdown), false, false)]
    [InlineData("worker-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, true)]
    [InlineData("worker-write-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, true)]
    [InlineData("runtime-close", nameof(FunctionRpcRelayTerminationReason.PeerClosed), true, false)]
    [InlineData("runtime-close", nameof(FunctionRpcRelayTerminationReason.PeerClosed), true, true)]
    [InlineData("runtime-cancel", nameof(FunctionRpcRelayTerminationReason.Canceled), true, false)]
    [InlineData("runtime-cancel", nameof(FunctionRpcRelayTerminationReason.Canceled), true, true)]
    [InlineData("runtime-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, false)]
    [InlineData("runtime-fault", nameof(FunctionRpcRelayTerminationReason.Faulted), true, true)]
    public async Task Relay_TerminationWithdrawsReadinessBeforeBlockedWriterReleases(
        string trigger, string expectedReason, bool expectedRemoval, bool drainBeforeTermination)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Channel<StreamingMessage> runtimeInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerOutbound = Channel.CreateUnbounded<StreamingMessage>();
        BlockingServerStreamWriter blockingWriter = new();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            CreateMessageReader(runtimeInbound.Reader), blockingWriter, runtimeCancellation.Token);
        IServerStreamWriter<StreamingMessage> workerWriter = string.Equals(trigger, "worker-write-fault", StringComparison.Ordinal)
            ? new GatedFaultingServerStreamWriter(Task.CompletedTask, new IOException("Injected worker write failure."))
            : CreateMessageWriter(workerOutbound.Writer);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInbound.Reader), workerWriter, workerCancellation.Token);
        Task stopTask = Task.CompletedTask;
        long expectedTerminalRevision = 0;

        try
        {
            await workerInbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
            await blockingWriter.WriteEntered.WaitAsync(timeout.Token);
            WorkerAssignment assignment = CreateWorkerAssignment();
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
            if (!string.Equals(trigger, "worker-write-fault", StringComparison.Ordinal))
            {
                await runtimeInbound.Writer.WriteAsync(new() { WorkerInitRequest = new() }, timeout.Token);
                Assert.NotNull((await workerOutbound.Reader.ReadAsync(timeout.Token)).WorkerInitRequest);
            }

            if (drainBeforeTermination)
            {
                Task<WorkerStatePollResult> drainPoll = manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);
                await runtimeInbound.Writer.WriteAsync(new() { WorkerDraining = new() }, timeout.Token);
                WorkerPodState draining = Assert.IsType<WorkerPodState>((await drainPoll).State);
                Assert.True(draining.IsDraining);
            }

            WorkerPodState assigned = manager.State;
            expectedTerminalRevision = assigned.Revision + 1;
            Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(assigned.Revision, timeout.Token);

            switch (trigger)
            {
                case "worker-close":
                    workerInbound.Writer.Complete();
                    break;
                case "runtime-close":
                    runtimeInbound.Writer.Complete();
                    break;
                case "worker-fault":
                    workerInbound.Writer.Complete(new IOException("Injected worker failure."));
                    break;
                case "runtime-fault":
                    runtimeInbound.Writer.Complete(new IOException("Injected runtime failure."));
                    break;
                case "worker-write-fault":
                    await runtimeInbound.Writer.WriteAsync(CreateMessage("worker-write-fault"), timeout.Token);
                    break;
                case "worker-cancel":
                    workerCancellation.Cancel();
                    break;
                case "runtime-cancel":
                    runtimeCancellation.Cancel();
                    break;
                case "shutdown":
                    stopTask = relay.StopAsync(timeout.Token);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(trigger));
            }

            WorkerPodState failed = Assert.IsType<WorkerPodState>((await poll).State);
            Assert.False(failed.IsWorkerReady);
            Assert.False(failed.IsWorkerAttached);
            Assert.Equal(WorkerPodStatus.RemovalRequested, failed.PodStatus);
            Assert.Equal(expectedRemoval, failed.RemovalRequested);
            bool runtimeTerminated = trigger.StartsWith("runtime-", StringComparison.Ordinal);
            Assert.Equal(runtimeTerminated ? WorkerAssignmentState.Unassigned : WorkerAssignmentState.Terminated, failed.AssignmentState);
            Assert.Equal(expectedRemoval && !runtimeTerminated, WorkerOriginState.FromState(failed).IsUnhealthy);
            Assert.Equal(expectedTerminalRevision, failed.Revision);
            Assert.Equal(runtimeTerminated ? WorkerAssignmentResult.WorkerNotReady : WorkerAssignmentResult.WorkerTerminated,
                manager.Assign(assignment));
            Assert.False(runtimeTask.IsCompleted);
            Assert.True(relay.IsAttached(FunctionRpcRelaySide.Runtime));
        }
        finally
        {
            blockingWriter.Release();
        }

        FunctionRpcRelayTerminalState[] terminalStates = await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
        Assert.All(terminalStates, state => Assert.Equal(expectedReason, state.Reason.ToString()));
        await stopTask.WaitAsync(timeout.Token);
        Assert.Equal(expectedTerminalRevision, manager.State.Revision);
    }

    [Fact]
    public async Task Relay_RuntimeOnlyTerminationBeforeProtocolInputRequestsRemoval()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        WorkerPodState initial = manager.State;
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(), new TestServerStreamWriter(), runtimeCancellation.Token);
        Assert.Same(initial, manager.State);

        runtimeCancellation.Cancel();
        FunctionRpcRelayTerminalState terminated = await runtimeTask.WaitAsync(timeout.Token);
        WorkerPodState removed = manager.State;
        Assert.Equal(FunctionRpcRelayTerminationReason.Canceled, terminated.Reason);
        Assert.True(removed.RemovalRequested);
        Assert.Equal(WorkerPodStatus.RemovalRequested, removed.PodStatus);
        Assert.False(removed.IsWorkerReady);
        Assert.Equal(initial.Revision + 1, removed.Revision);

        await Assert.ThrowsAsync<FunctionRpcRelayAttachmentException>(() => relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(CreateStartStream()), new TestServerStreamWriter(), timeout.Token));
        Assert.Same(removed, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relay_CanceledPreInitRuntimeCannotSendLateMessagesAfterRemoval(bool startStreamBeforeRuntime)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        manager.Assign(CreateWorkerAssignment());
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> workerInput = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerOutput = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> oldOutput = Channel.CreateUnbounded<StreamingMessage>();
        PumpingSynchronizationContext readContext = new();
        Task<FunctionRpcRelayTerminalState> worker = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInput.Reader), CreateMessageWriter(workerOutput.Writer), timeout.Token);
        StreamingMessage start = CreateStartStream();
        if (startStreamBeforeRuntime)
        {
            await workerInput.Writer.WriteAsync(start, timeout.Token);
        }

        TaskCompletionSource<bool> lateRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource observed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IAsyncStreamReader<StreamingMessage>> oldReader = new();
        oldReader.Setup(reader => reader.MoveNext(It.IsAny<CancellationToken>())).Returns(lateRead.Task);
        oldReader.SetupGet(reader => reader.Current).Returns(() =>
        {
            observed.TrySetResult();
            return new StreamingMessage { RequestId = "stale-runtime", WorkerInitRequest = new() };
        });
        using CancellationTokenSource oldCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Task<FunctionRpcRelayTerminalState> old;
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(readContext);
            old = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
                oldReader.Object, CreateMessageWriter(oldOutput.Writer), oldCancellation.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        try
        {
            if (!startStreamBeforeRuntime)
            {
                await workerInput.Writer.WriteAsync(start, timeout.Token);
            }

            // Forwarding StartStream can resume on the same context as the deliberately delayed read.
            Task<StreamingMessage> forwardedStart = oldOutput.Reader.ReadAsync(timeout.Token).AsTask();
            await readContext.RunUntilAsync(forwardedStart, timeout.Token);
            Assert.Equal(start, await forwardedStart);
            WorkerPodState accepted = manager.State;

            oldCancellation.Cancel();
            await readContext.RunUntilAsync(Task.WhenAll(old, worker), timeout.Token);
            Assert.Equal(FunctionRpcRelayTerminationReason.Canceled, (await old).Reason);
            WorkerPodState removed = manager.State;
            Assert.True(removed.RemovalRequested);
            Assert.Equal(accepted.Revision + 1, removed.Revision);
            Assert.False(relay.IsAttached(FunctionRpcRelaySide.Worker));
            Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));
            await Assert.ThrowsAsync<FunctionRpcRelayAttachmentException>(() => relay.AttachAsync(FunctionRpcRelaySide.Runtime,
                new BlockingStreamReader(), new TestServerStreamWriter(), timeout.Token));

            lateRead.SetResult(true);
            await readContext.RunUntilAsync(observed.Task, timeout.Token);
            oldReader.VerifyGet(reader => reader.Current, Times.Once);
            Assert.False(workerOutput.Reader.TryRead(out _));
            Assert.Same(removed, manager.State);
            Assert.Equal(WorkerAssignmentResult.WorkerNotReady, manager.Assign(CreateWorkerAssignment()));
        }
        finally
        {
            timeout.Cancel();
            lateRead.TrySetResult(true);
            using CancellationTokenSource cleanupTimeout = new(TestTimeout);
            await readContext.RunUntilAsync(Task.WhenAll(old, worker), cleanupTimeout.Token);
        }
    }

    [Fact]
    public async Task Relay_DelayedStartStreamCannotChangeReplacementReadiness()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        TaskCompletionSource<bool> delayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> currentRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PumpingSynchronizationContext readContext = new();
        Mock<IAsyncStreamReader<StreamingMessage>> oldReader = new(MockBehavior.Strict);
        oldReader.Setup(reader => reader.MoveNext(It.IsAny<CancellationToken>())).Returns(delayedRead.Task);
        oldReader.SetupGet(reader => reader.Current).Returns(() =>
        {
            currentRead.TrySetResult(true);
            return new StreamingMessage { StartStream = new() { WorkerId = "old-worker" } };
        });

        Task<FunctionRpcRelayTerminalState> oldWorker;
        SynchronizationContext? previousContext = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(readContext);
            oldWorker = relay.AttachAsync(FunctionRpcRelaySide.Worker,
                oldReader.Object, new TestServerStreamWriter(), workerCancellation.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Task<FunctionRpcRelayTerminalState> oldRuntime = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(), new TestServerStreamWriter(), timeout.Token);

        try
        {
            workerCancellation.Cancel();
            await readContext.RunUntilAsync(Task.WhenAll(oldRuntime, oldWorker), timeout.Token);
            Assert.False(manager.State.IsWorkerReady);

            Task<FunctionRpcRelayTerminalState> replacement = relay.AttachAsync(FunctionRpcRelaySide.Worker,
                new SingleMessageThenBlockStreamReader(CreateStartStream()), new TestServerStreamWriter(), timeout.Token);
            WorkerPodState ready = manager.State;
            Assert.True(ready.IsWorkerReady);

            delayedRead.SetResult(true);
            // Current only marks entry. The pump returns after the entire callback, including
            // ProcessInboundMessage and the stale reader's return, has finished.
            await readContext.RunUntilAsync(currentRead.Task, timeout.Token);
            oldReader.VerifyGet(reader => reader.Current, Times.Once);
            Assert.Same(ready, manager.State);
            Assert.Equal("test-worker", manager.State.WorkerId);

            await relay.StopAsync(timeout.Token);
            await replacement.WaitAsync(timeout.Token);
        }
        finally
        {
            delayedRead.TrySetResult(true);
        }
    }

    private static WorkerAssignment CreateWorkerAssignment(
        string functionGroupName = "test-group",
        int? maxHttpConcurrency = null,
        string? httpProxyEndpoint = null,
        bool isAlwaysReady = false)
        => new(WorkerStartupMode.SpecializationRequired, "test-app", functionGroupName, isAlwaysReady,
            environment: new Dictionary<string, string>(), functionAppDirectory: "/home/site/wwwroot", maxHttpConcurrency: maxHttpConcurrency,
            httpProxyEndpoint: httpProxyEndpoint is null ? null : new Uri($"https://{httpProxyEndpoint}"));

    private sealed class PumpingSynchronizationContext(bool flowToCallbacks = true) : SynchronizationContext
    {
        private readonly Channel<(SendOrPostCallback Callback, object? State)> _callbacks =
            Channel.CreateUnbounded<(SendOrPostCallback, object?)>();

        public override void Post(SendOrPostCallback callback, object? state) =>
            _callbacks.Writer.TryWrite((callback, state));

        public async Task RunUntilAsync(Task completion, CancellationToken cancellationToken)
        {
            // Wake a waiting pump even when completion happens outside a queued callback.
            _ = completion.ContinueWith(
                _ => Post(static _ => { }, null),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

            while (!completion.IsCompleted)
            {
                (SendOrPostCallback callback, object? state) = await _callbacks.Reader.ReadAsync(cancellationToken);
                SynchronizationContext? previousContext = Current;
                try
                {
                    SetSynchronizationContext(flowToCallbacks ? this : null);
                    callback(state);
                }
                finally
                {
                    SetSynchronizationContext(previousContext);
                }
            }

            await completion.WaitAsync(cancellationToken);
        }
    }
}
