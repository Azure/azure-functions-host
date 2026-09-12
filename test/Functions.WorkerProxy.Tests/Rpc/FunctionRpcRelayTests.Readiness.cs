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
    public async Task Relay_ReadinessRequiresStartStreamButNotRuntimeInitialization(bool runtimeFirst)
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
        Assert.Equal(WorkerAssignmentResult.WorkerNotReady, manager.Assign(CreateWorkerAssignment()));

        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInbound.Reader), CreateMessageWriter(workerOutbound.Writer), timeout.Token);
        WorkerPodState attached = manager.State;
        Assert.Equal(1, attached.Revision);
        Assert.True(attached.IsWorkerAttached);
        Assert.False(attached.IsWorkerReady);
        Assert.Null(attached.WorkerId);

        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(attached.Revision, timeout.Token);
        StreamingMessage start = CreateStartStream();
        await workerInbound.Writer.WriteAsync(start, timeout.Token);
        WorkerPodState ready = Assert.IsType<WorkerPodState>((await poll).State);
        Assert.Equal(2, ready.Revision);
        Assert.True(ready.IsWorkerReady);
        Assert.Equal(start.StartStream.WorkerId, ready.WorkerId);
        Assert.Equal(WorkerPodStatus.None, ready.PodStatus);

        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        Assert.Equal(WorkerPodStatus.ReadyForRequest, manager.State.PodStatus);
        Assert.Equal(3, manager.State.Revision);
        Assert.False(workerOutbound.Reader.TryRead(out _));

        runtimeTask ??= relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            CreateMessageReader(runtimeInbound.Reader), CreateMessageWriter(runtimeOutbound.Writer), timeout.Token);
        Assert.Same(start, await runtimeOutbound.Reader.ReadAsync(timeout.Token));

        StreamingMessage init = new()
        {
            RequestId = "runtime-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtimeInbound.Writer.WriteAsync(init, timeout.Token);
        Assert.Same(init, await workerOutbound.Reader.ReadAsync(timeout.Token));
        Assert.False(workerOutbound.Reader.TryRead(out _));
        Assert.Equal(3, manager.State.Revision);

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
    [InlineData("worker-close", nameof(FunctionRpcRelayTerminationReason.PeerClosed))]
    [InlineData("runtime-close", nameof(FunctionRpcRelayTerminationReason.PeerClosed))]
    [InlineData("worker-fault", nameof(FunctionRpcRelayTerminationReason.Faulted))]
    [InlineData("worker-cancel", nameof(FunctionRpcRelayTerminationReason.Canceled))]
    [InlineData("runtime-cancel", nameof(FunctionRpcRelayTerminationReason.Canceled))]
    [InlineData("shutdown", nameof(FunctionRpcRelayTerminationReason.Shutdown))]
    public async Task Relay_TerminationWithdrawsReadinessBeforeBlockedWriterReleases(
        string trigger, string expectedReason)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Channel<StreamingMessage> runtimeInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> workerInbound = Channel.CreateUnbounded<StreamingMessage>();
        BlockingServerStreamWriter blockingWriter = new();
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            CreateMessageReader(runtimeInbound.Reader), blockingWriter, runtimeCancellation.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(workerInbound.Reader), new TestServerStreamWriter(), workerCancellation.Token);
        Task stopTask = Task.CompletedTask;

        try
        {
            await workerInbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
            await blockingWriter.WriteEntered.WaitAsync(timeout.Token);
            WorkerAssignment assignment = CreateWorkerAssignment();
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
            WorkerPodState assigned = manager.State;
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
            Assert.Equal(WorkerPodStatus.None, failed.PodStatus);
            Assert.Equal(WorkerAssignmentState.Failed, failed.AssignmentState);
            Assert.Equal(assigned.Revision + 1, failed.Revision);
            Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(assignment));
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
        Assert.Equal(4, manager.State.Revision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_ReplacementReadinessRespectsTerminalAssignment(bool assigned)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerAssignment assignment = CreateWorkerAssignment();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "original", timeout.Token);
        Assert.True(manager.State.IsWorkerReady);

        if (assigned)
        {
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        }

        await worker.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(runtime.WaitForTerminationAsync(timeout.Token), worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        WorkerPodState terminated = manager.State;
        Assert.False(terminated.IsWorkerReady);

        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(replacementRuntime, replacementWorker, "replacement", timeout.Token);

        if (assigned)
        {
            Assert.Same(terminated, manager.State);
            Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(assignment));
        }
        else
        {
            Assert.True(manager.State.IsWorkerReady);
            Assert.True(manager.State.SessionId > terminated.SessionId);
            Assert.Equal(terminated.Revision + 2, manager.State.Revision);
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        }
    }

    [Fact]
    public async Task Relay_RuntimeOnlyTerminationDoesNotPreventNextWorkerSession()
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
        Assert.Same(initial, manager.State);

        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(CreateStartStream()), new TestServerStreamWriter(), timeout.Token);
        Assert.True(manager.State.IsWorkerReady);
        Assert.True(manager.State.SessionId > terminated.SessionId);
        Assert.Equal(2, manager.State.Revision);
        await relay.StopAsync(timeout.Token);
        await workerTask.WaitAsync(timeout.Token);
    }

    [Fact]
    public async Task Relay_DelayedStartStreamCannotChangeReplacementReadiness()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
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
                oldReader.Object, new TestServerStreamWriter(), timeout.Token);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }

        Task<FunctionRpcRelayTerminalState> oldRuntime = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(), new TestServerStreamWriter(), runtimeCancellation.Token);

        try
        {
            runtimeCancellation.Cancel();
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

    private static WorkerAssignment CreateWorkerAssignment()
        => new("test-app", "test-group", isAlwaysReady: false,
            environment: new Dictionary<string, string>(), functionAppDirectory: "/home/site/wwwroot");

    private sealed class PumpingSynchronizationContext : SynchronizationContext
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
                    SetSynchronizationContext(this);
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
