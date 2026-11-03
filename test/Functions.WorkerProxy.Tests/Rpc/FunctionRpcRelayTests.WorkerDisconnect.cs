// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WorkerEventStream_EofWithoutRuntimeRequestsRemoval(bool assigned)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> inbound = Channel.CreateUnbounded<StreamingMessage>();
        await inbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(inbound.Reader), new TestServerStreamWriter(), timeout.Token);
        Assert.True(manager.State.IsWorkerReady);
        if (assigned)
        {
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        }

        long revision = manager.State.Revision;
        inbound.Writer.Complete();
        FunctionRpcRelayTerminalState terminal = await workerTask.WaitAsync(timeout.Token);

        Assert.Equal(FunctionRpcRelaySide.Worker, terminal.Side);
        Assert.Equal(FunctionRpcRelayTerminationReason.PeerClosed, terminal.Reason);
        Assert.True(manager.State.RemovalRequested);
        Assert.Equal(revision + 1, manager.State.Revision);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Null(manager.State.SessionId);
        Assert.Null(manager.State.WorkerId);
        Assert.Null(manager.State.FunctionGroupName);
        Assert.False(manager.State.RuntimeInitialized);
        Assert.False(manager.RequestRemoval());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WorkerEventStream_ReadOrWriteFailureRequestsRemoval(bool writeFailure, bool assigned)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        IOException expectedException = new("Injected Worker stream failure.");
        Channel<StreamingMessage> workerInbound = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> runtimeInbound = Channel.CreateUnbounded<StreamingMessage>();
        Mock<IServerStreamWriter<StreamingMessage>> writer = new(MockBehavior.Strict);
        writer.Setup(instance => instance.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);
        await workerInbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(workerInbound.Reader), writer.Object, timeout.Token);
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime, CreateMessageReader(runtimeInbound.Reader), new TestServerStreamWriter(), timeout.Token);
        Assert.True(manager.State.IsWorkerReady);
        if (assigned)
        {
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        }

        long revision = manager.State.Revision;
        if (writeFailure)
        {
            await runtimeInbound.Writer.WriteAsync(CreateMessage("trigger-worker-write"), timeout.Token);
        }
        else
        {
            workerInbound.Writer.Complete(expectedException);
        }

        FunctionRpcRelayTerminalState[] states = await Task.WhenAll(workerTask, runtimeTask).WaitAsync(timeout.Token);
        Assert.All(states, state =>
        {
            Assert.Equal(FunctionRpcRelaySide.Worker, state.Side);
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, state.Reason);
            Assert.Same(expectedException, state.Exception);
        });
        Assert.True(manager.State.RemovalRequested);
        Assert.False(manager.State.IsWorkerReady);
        Assert.Equal(revision + 1, manager.State.Revision);
        writer.Verify(instance => instance.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()),
            writeFailure ? Times.Once() : Times.Never());
    }

    [Theory]
    [InlineData("eof")]
    [InlineData("cancel")]
    [InlineData("read-fault")]
    [InlineData("write-fault")]
    public async Task WorkerEventStream_TerminationBeforeStartStreamAllowsReplacement(string trigger)
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        Channel<StreamingMessage> inbound = Channel.CreateUnbounded<StreamingMessage>();
        Mock<IServerStreamWriter<StreamingMessage>> writer = new(MockBehavior.Strict);
        writer.Setup(instance => instance.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("Injected pre-StartStream write failure."));
        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime,
            new SingleMessageThenBlockStreamReader(CreateMessage("queued-before-worker-start")),
            new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(inbound.Reader),
            string.Equals(trigger, "write-fault", StringComparison.Ordinal) ? writer.Object : new TestServerStreamWriter(),
            workerCancellation.Token);

        switch (trigger)
        {
            case "eof":
                inbound.Writer.Complete();
                break;
            case "cancel":
                workerCancellation.Cancel();
                break;
            case "read-fault":
                inbound.Writer.Complete(new IOException("Injected pre-StartStream read failure."));
                break;
            case "write-fault":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        FunctionRpcRelayTerminalState[] states = await Task.WhenAll(workerTask, runtimeTask).WaitAsync(timeout.Token);
        Assert.All(states, state => Assert.Equal(FunctionRpcRelaySide.Worker, state.Side));
        Assert.False(manager.State.RemovalRequested);
        Assert.False(manager.State.IsWorkerReady);

        Task<FunctionRpcRelayTerminalState> replacement = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, new SingleMessageThenBlockStreamReader(CreateStartStream()),
            new TestServerStreamWriter(), timeout.Token);
        Assert.True(manager.State.IsWorkerReady);
        await relay.StopAsync(timeout.Token);
        await replacement.WaitAsync(timeout.Token);
        Assert.False(manager.State.RemovalRequested);
    }

    [Fact]
    public async Task WorkerEventStream_RejectedStartStreamDoesNotAdmitHandlerForRemoval()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        Assert.True(manager.OnWorkerAttached(2));
        WorkerPodState current = manager.State;
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> inbound = Channel.CreateUnbounded<StreamingMessage>();
        await inbound.Writer.WriteAsync(CreateStartStream(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, CreateMessageReader(inbound.Reader), new TestServerStreamWriter(), timeout.Token);
        Assert.Same(current, manager.State);

        inbound.Writer.Complete();
        FunctionRpcRelayTerminalState terminal = await workerTask.WaitAsync(timeout.Token);

        Assert.Equal(FunctionRpcRelayTerminationReason.PeerClosed, terminal.Reason);
        Assert.Same(current, manager.State);
        Assert.False(manager.State.RemovalRequested);
    }
}
