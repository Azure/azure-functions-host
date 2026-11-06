// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task Relay_RuntimeLossRetainsAcceptedIdentityAfterRemoval()
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        await InitializeWorkerAsync(worker, runtime, manager, null, timeout.Token);
        WorkerPodState assigned = manager.State;

        await runtime.CompleteRequestAsync(timeout.Token);
        await runtime.WaitForTerminationAsync(timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Count(r =>
            string.Equals(r.EventName, "PeerReleased", StringComparison.Ordinal)) == 2, timeout.Token);

        Assert.True(manager.State.RemovalRequested);
        Assert.Null(manager.State.FunctionAppName);
        CapturedProxyLog terminal = Assert.Single(generator.ProxyRecords, r =>
            r.EventName is "SessionTerminated" or "SessionFaulted");
        Assert.Equal("Runtime", terminal.Context!.Side);
        Assert.Equal("Read", terminal.Context.Phase);
        Assert.Equal("PeerClosed", terminal.Context.ReasonCode);
        Assert.Equal(assigned.FunctionAppName, terminal.Context.State!.FunctionAppName);
        Assert.Equal(assigned.WorkerId, terminal.Context.WorkerId);
        Assert.Equal(assigned.SessionId, terminal.Context.ProxySessionId);
        Assert.Equal(manager.State.Revision, terminal.Context.EventRevision);
        Assert.True(terminal.Context.EventRevision > terminal.Context.State.Revision);
        Assert.All(generator.ProxyRecords.Where(r => string.Equals(r.EventName, "PeerReleased", StringComparison.Ordinal)),
            record => Assert.Equal(assigned.FunctionAppName, record.Context!.State!.FunctionAppName));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_WriteFailureLogsOnlyTheMessageBeingWritten(bool response)
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = new(factory.Services.GetRequiredService<ILogger<FunctionRpcRelay>>(),
            CreateCapabilityProvider(), manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        Channel<StreamingMessage> fromWorker = Channel.CreateUnbounded<StreamingMessage>();
        Channel<StreamingMessage> fromRuntime = Channel.CreateUnbounded<StreamingMessage>();
        IOException expected = new("private-exception-payload");
        Mock<IServerStreamWriter<StreamingMessage>> writer = new();
        writer.Setup(w => w.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .Returns((StreamingMessage message, CancellationToken _) =>
                message.InvocationResponse is not null || message.InvocationRequest is not null
                    ? Task.FromException(expected) : Task.CompletedTask);
        Task<FunctionRpcRelayTerminalState> runtime = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            CreateMessageReader(fromRuntime.Reader), response ? writer.Object : new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> worker = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            CreateMessageReader(fromWorker.Reader), response ? new TestServerStreamWriter() : writer.Object, timeout.Token);
        await fromWorker.Writer.WriteAsync(CreateStartStream(), timeout.Token);
        await WaitForLogAsync(() => manager.State.WorkerId is not null, timeout.Token);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));
        StreamingMessage message = new() { RequestId = "protocol-request" };
        if (response)
        {
            message.InvocationResponse = new()
            {
                InvocationId = "invocation-id",
                Result = new() { Status = StatusResult.Types.Status.Success },
                ReturnValue = new() { String = "private-response-payload" }
            };
        }
        else
        {
            message.InvocationRequest = new()
            {
                InvocationId = "invocation-id",
                FunctionId = "function-id"
            };
        }

        await (response ? fromWorker : fromRuntime).Writer.WriteAsync(message, timeout.Token);
        FunctionRpcRelayTerminalState[] results = await Task.WhenAll(runtime, worker).WaitAsync(timeout.Token);

        Assert.Same(expected, results[0].Exception);
        Assert.Same(results[0], results[1]);
        CapturedProxyLog failure = Assert.Single(generator.ProxyRecords, r =>
            r.EventName is "SessionFaulted" or "SessionTerminated");
        Assert.Equal("Write", failure.Context!.Phase);
        Assert.Equal(response ? "Runtime" : "Worker", failure.Context.Side);
        Assert.Equal("protocol-request", failure.Context.RequestId);
        Assert.Equal("invocation-id", failure.Context.InvocationId);
        Assert.Equal(response ? null : "function-id", failure.Context.FunctionId);
        Assert.DoesNotContain(generator.ProxyRecords, r => r.ToString().Contains("private-", StringComparison.Ordinal));
        Assert.DoesNotContain(generator.ProxyRecords, r => string.Equals(r.EventName, "InvocationResponseFailed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relay_CancellationLogsKnownObservationWithoutInferringHealthyShutdown(bool callCanceled)
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = new(factory.Services.GetRequiredService<ILogger<FunctionRpcRelay>>(),
            CreateCapabilityProvider(), manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource call = new();
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<FunctionRpcRelayTerminalState> worker = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new GatedFaultingStreamReader(release.Task, new OperationCanceledException("private-cancellation"), CreateStartStream()),
            new TestServerStreamWriter(), call.Token);
        if (callCanceled)
        {
            call.Cancel();
        }
        else
        {
            release.TrySetResult();
        }

        FunctionRpcRelayTerminalState terminal = await worker.WaitAsync(timeout.Token);
        CapturedProxyLog record = Assert.Single(generator.ProxyRecords, r =>
            r.EventName is "SessionFaulted" or "SessionTerminated");
        Assert.Equal(FunctionRpcRelayTerminationReason.Canceled, terminal.Reason);
        Assert.Equal("Worker", record.Context!.Side);
        Assert.Equal("Canceled", record.Context.ReasonCode);
        Assert.Equal(callCanceled ? "PeerCall" : "Unknown", record.Context.CancellationSource);
        Assert.NotEqual("Shutdown", record.Context.CancellationSource);
        Assert.Equal(LogLevel.Information, record.Level);
    }

    [Theory]
    [InlineData(StatusResult.Types.Status.Success, 0)]
    [InlineData(StatusResult.Types.Status.Failure, 1)]
    [InlineData(StatusResult.Types.Status.Cancelled, 1)]
    [InlineData(null, 1)]
    public async Task Relay_InvocationResponsesLogOnlyObservedUnsuccessfulResults(StatusResult.Types.Status? status, int expectedCount)
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await worker.WriteAsync(CreateStartStream(), timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage response = new()
        {
            RequestId = "request-id",
            InvocationResponse = new()
            {
                InvocationId = "invocation-id",
                Result = status is { } value ? new() { Status = value } : null,
                ReturnValue = new() { String = "private-return" }
            }
        };
        await worker.WriteAsync(response, timeout.Token);
        Assert.Equal(response, await runtime.ReadAsync(timeout.Token));

        CapturedProxyLog[] records = generator.ProxyRecords.Where(r =>
            string.Equals(r.EventName, "InvocationResponseFailed", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expectedCount, records.Length);
        Assert.All(records, record =>
        {
            Assert.Equal("invocation-id", record.Context!.InvocationId);
            Assert.Equal("ResponseReceived", record.Context.Phase);
            Assert.Equal(status is null ? "MissingResult" : "ProtocolResult", record.Context.ReasonCode);
            Assert.Null(record.Context.FunctionId);
            Assert.Empty(record.Details);
        });
        Assert.DoesNotContain(generator.ProxyRecords, r => r.ToString().Contains("private-return", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Relay_LateFailureCannotAcquireReplacementAssignmentIdentity()
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = new(factory.Services.GetRequiredService<ILogger<FunctionRpcRelay>>(),
            CreateCapabilityProvider(), manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource oldCall = new();
        TaskCompletionSource<bool> delayedRead = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IAsyncStreamReader<StreamingMessage>> oldReader = new();
        StreamingMessage oldStart = CreateStartStream();
        oldStart.StartStream.WorkerId = "old-worker";
        oldReader.SetupGet(reader => reader.Current).Returns(oldStart);
        oldReader.SetupSequence(reader => reader.MoveNext(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).Returns(delayedRead.Task);
        Task<FunctionRpcRelayTerminalState> oldWorker = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            oldReader.Object, new TestServerStreamWriter(), oldCall.Token);
        long oldSession = manager.State.SessionId!.Value;
        oldCall.Cancel();
        await oldWorker.WaitAsync(timeout.Token);
        StreamingMessage newStart = CreateStartStream();
        newStart.StartStream.WorkerId = "new-worker";
        Task<FunctionRpcRelayTerminalState> replacement = relay.AttachAsync(FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(newStart), new TestServerStreamWriter(), timeout.Token);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment("new-group")));
        try
        {
            delayedRead.TrySetException(new IOException("private-late-error"));
            await WaitForLogAsync(() => generator.ProxyRecords.Any(r =>
                string.Equals(r.EventName, "SecondaryStreamFailure", StringComparison.Ordinal)), timeout.Token);
            CapturedProxyLog late = Assert.Single(generator.ProxyRecords, r =>
                string.Equals(r.EventName, "SecondaryStreamFailure", StringComparison.Ordinal));
            Assert.Equal(oldSession, late.Context!.ProxySessionId);
            Assert.Equal("old-worker", late.Context.WorkerId);
            Assert.Null(late.Context.State!.FunctionAppName);
            Assert.Null(late.Context.State.FunctionGroupName);
            Assert.Equal("new-worker", manager.State.WorkerId);
            Assert.Equal("new-group", manager.State.FunctionGroupName);
            Assert.Single(generator.ProxyRecords, r => r.EventName is "SessionTerminated" or "SessionFaulted");
        }
        finally
        {
            delayedRead.TrySetResult(false);
            await relay.StopAsync(timeout.Token);
            await replacement.WaitAsync(timeout.Token);
        }
    }
}
