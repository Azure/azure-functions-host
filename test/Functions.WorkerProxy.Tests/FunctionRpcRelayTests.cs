// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string FunctionRpcServiceName = "AzureFunctionsRpcMessages.FunctionRpc";
    private const string EventStreamMethodName = "EventStream";
    private static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(30);

    private static readonly Marshaller<StreamingMessage> StreamingMessageMarshaller =
        Marshallers.Create(static message => message.ToByteArray(), static payload => StreamingMessage.Parser.ParseFrom(payload));

    private static readonly Method<StreamingMessage, StreamingMessage> EventStreamMethod =
        new(MethodType.DuplexStreaming, FunctionRpcServiceName, EventStreamMethodName, StreamingMessageMarshaller, StreamingMessageMarshaller);

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FunctionRpc_IsAvailableOnBothRpcListeners(bool runtimeListener)
    {
        FunctionRpcRelaySide side = runtimeListener ? FunctionRpcRelaySide.Runtime : FunctionRpcRelaySide.Worker;
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient client = CreateClient(factory, side, timeout.Token);

        await client.WriteAsync(CreateMessage("attach"), timeout.Token);

        await WaitForAttachmentAsync(relay, side, timeout.Token);
        Assert.True(relay.IsAttached(side));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_ConnectsEitherSideFirstAndForwardsBothDirections(bool runtimeConnectsFirst)
    {
        FunctionRpcRelaySide firstSide = runtimeConnectsFirst ? FunctionRpcRelaySide.Runtime : FunctionRpcRelaySide.Worker;
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        StreamingMessage runtimeMessage = new()
        {
            RequestId = "runtime-first",
            WorkerInitRequest = new WorkerInitRequest { HostVersion = "test-host" }
        };
        StreamingMessage workerMessage = new()
        {
            RequestId = "worker-first",
            StartStream = new StartStream { WorkerId = "test-worker" }
        };

        if (firstSide == FunctionRpcRelaySide.Runtime)
        {
            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await runtime.WriteAsync(runtimeMessage, timeout.Token);
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
            Assert.False(relay.IsAttached(FunctionRpcRelaySide.Worker));

            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await worker.WriteAsync(workerMessage, timeout.Token);
            Assert.Equal(runtimeMessage, await worker.ReadAsync(timeout.Token));
            Assert.Equal(workerMessage, await runtime.ReadAsync(timeout.Token));
        }
        else
        {
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await worker.WriteAsync(workerMessage, timeout.Token);
            await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);
            Assert.False(relay.IsAttached(FunctionRpcRelaySide.Runtime));

            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await runtime.WriteAsync(runtimeMessage, timeout.Token);
            Assert.Equal(runtimeMessage, await worker.ReadAsync(timeout.Token));
            Assert.Equal(workerMessage, await runtime.ReadAsync(timeout.Token));
        }
    }

    [Fact]
    public async Task Relay_ConcurrentBidirectionalProducersPreservePerSideOrdering()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        IReadOnlyList<StreamingMessage> runtimeMessages = CreateMessages("runtime", count: 64);
        IReadOnlyList<StreamingMessage> workerMessages = CreateMessages("worker", count: 64);

        Task runtimeWrites = runtime.WriteAllAsync(runtimeMessages, timeout.Token);
        Task workerWrites = worker.WriteAllAsync(workerMessages, timeout.Token);
        Task<IReadOnlyList<StreamingMessage>> workerReads = worker.ReadAsync(runtimeMessages.Count, timeout.Token);
        Task<IReadOnlyList<StreamingMessage>> runtimeReads = runtime.ReadAsync(workerMessages.Count, timeout.Token);
        await Task.WhenAll(runtimeWrites, workerWrites, workerReads, runtimeReads);

        Assert.Equal(runtimeMessages.Select(static message => message.RequestId), workerReads.Result.Select(static message => message.RequestId));
        Assert.Equal(workerMessages.Select(static message => message.RequestId), runtimeReads.Result.Select(static message => message.RequestId));
    }

    [Fact]
    public async Task Relay_MessagesQueuedBeforePeerConnectsAreDeliveredInOrder()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        string payload = new('x', 64 * 1024);
        IReadOnlyList<StreamingMessage> sentMessages = Enumerable.Range(0, 64)
            .Select(index => CreateMessage($"queued-{index}", payload))
            .ToArray();
        await runtime.WriteAllAsync(sentMessages, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        Assert.False(relay.IsAttached(FunctionRpcRelaySide.Worker));

        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await worker.WriteAsync(CreateMessage("worker-attach"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);

        IReadOnlyList<StreamingMessage> receivedMessages = await worker.ReadAsync(sentMessages.Count, timeout.Token);
        Assert.Equal(sentMessages.Select(static message => message.RequestId), receivedMessages.Select(static message => message.RequestId));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_RejectsDuplicateSideAttachment(bool duplicateRuntime)
    {
        FunctionRpcRelaySide side = duplicateRuntime ? FunctionRpcRelaySide.Runtime : FunctionRpcRelaySide.Worker;
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient first = CreateClient(factory, side, timeout.Token);
        await first.WriteAsync(CreateMessage("first"), timeout.Token);
        await WaitForAttachmentAsync(relay, side, timeout.Token);

        await using RelayClient duplicate = CreateClient(factory, side, timeout.Token);
        GrpcRpcException exception = await duplicate.WriteAndReadRejectionAsync(CreateMessage("duplicate"), timeout.Token);

        Assert.Equal(StatusCode.AlreadyExists, exception.StatusCode);
        Assert.True(relay.IsAttached(side));
        Assert.Null(relay.LastTerminalState);
    }

    [Fact]
    public async Task Relay_PeerCloseStopsSiblingAndAllowsWholeSessionReplacement()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);

        await using (RelayClient firstRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        await using (RelayClient firstWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            await ExchangeAsync(firstRuntime, firstWorker, "first", timeout.Token);
            await firstRuntime.CompleteRequestAsync(timeout.Token);

            StatusCode[] terminalStatuses = await Task.WhenAll(firstRuntime.WaitForTerminationAsync(timeout.Token),
                firstWorker.WaitForTerminationAsync(timeout.Token));
            Assert.All(terminalStatuses, static status => Assert.Equal(StatusCode.Unavailable, status));
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Equal(FunctionRpcRelayTerminationReason.PeerClosed, relay.LastTerminalState?.Reason);
        Assert.Equal(FunctionRpcRelaySide.Runtime, relay.LastTerminalState?.Side);

        await using RelayClient secondRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient secondWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(secondRuntime, secondWorker, "replacement", timeout.Token);
    }

    [Fact]
    public async Task Relay_ReconnectDuringSessionTeardownReturnsUnavailable()
    {
        using BlockingLogger<FunctionRpcRelay> logger = new();
        await using WorkerProxyWebApplicationFactory factory = new(
            configureServices: services => services.AddSingleton<Microsoft.Extensions.Logging.ILogger<FunctionRpcRelay>>(logger));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "teardown", timeout.Token);

        await runtime.CompleteRequestAsync(timeout.Token);
        try
        {
            await logger.LogEntered.WaitAsync(timeout.Token);
            await using RelayClient reconnect = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);

            GrpcRpcException exception = await reconnect.WriteAndReadRejectionAsync(CreateMessage("reconnect"), timeout.Token);

            Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        }
        finally
        {
            logger.Release();
        }

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
    }

    [Fact]
    public async Task Relay_CallCancellationPropagatesToSibling()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, runtimeCancellation.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "cancel", timeout.Token);

        runtimeCancellation.Cancel();

        StatusCode workerStatus = await worker.WaitForTerminationAsync(timeout.Token);
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Contains(workerStatus, new[] { StatusCode.Cancelled, StatusCode.Unavailable });
        Assert.Equal(FunctionRpcRelayTerminationReason.Canceled, relay.LastTerminalState?.Reason);
        Assert.Equal(FunctionRpcRelaySide.Runtime, relay.LastTerminalState?.Side);
    }

    [Fact]
    public async Task Relay_FirstReadFaultStopsBothStreamOperationsAndPreservesFault()
    {
        FunctionRpcRelay relay = CreateInProcessRelay();
        using CancellationTokenSource timeout = new(TestTimeout);
        TaskCompletionSource<bool> releaseFault = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IOException expectedException = new("Injected read failure.");
        GatedFaultingStreamReader faultingReader = new(releaseFault.Task, expectedException);
        BlockingStreamReader blockingReader = new();
        TestServerStreamWriter responseWriter = new();
        TestServerStreamWriter workerResponseWriter = new();

        Task<FunctionRpcRelayTerminalState> runtimeTask =
            relay.AttachAsync(FunctionRpcRelaySide.Runtime, faultingReader, responseWriter, timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask =
            relay.AttachAsync(FunctionRpcRelaySide.Worker, blockingReader, workerResponseWriter, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);

        releaseFault.TrySetResult(true);

        FunctionRpcRelayTerminalState runtimeState = await runtimeTask.WaitAsync(timeout.Token);
        FunctionRpcRelayTerminalState workerState = await workerTask.WaitAsync(timeout.Token);
        Assert.Same(expectedException, runtimeState.Exception);
        Assert.Same(runtimeState, workerState);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, runtimeState.Reason);
        Assert.Equal(FunctionRpcRelaySide.Runtime, runtimeState.Side);

        await relay.DisposeAsync();
    }

    [Fact]
    public async Task Relay_CanceledStopWaitDoesNotCancelSharedStop()
    {
        using BlockingLogger<FunctionRpcRelay> logger = new();
        FunctionRpcRelay relay = new(logger);
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource stopCancellation = new();
        BlockingServerStreamWriter blockingWriter = new();

        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new SingleMessageThenBlockStreamReader(CreateMessage("block-stop")), new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask =
            relay.AttachAsync(FunctionRpcRelaySide.Worker, new BlockingStreamReader(), blockingWriter, timeout.Token);
        await blockingWriter.WriteEntered.WaitAsync(timeout.Token);

        Task stopTask = Task.Run(() => relay.StopAsync(stopCancellation.Token), timeout.Token);
        try
        {
            await logger.LogEntered.WaitAsync(timeout.Token);
            stopCancellation.Cancel();
            logger.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => stopTask.WaitAsync(timeout.Token));

            Task disposeTask = relay.DisposeAsync().AsTask();
            Assert.False(disposeTask.IsCompleted);
            blockingWriter.Release();

            FunctionRpcRelayTerminalState[] terminalStates =
                await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
            Assert.All(terminalStates, static state => Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, state.Reason));
            await disposeTask.WaitAsync(timeout.Token);
            Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        }
        finally
        {
            logger.Release();
            blockingWriter.Release();
        }
    }

    [Fact]
    public async Task Relay_ApplicationShutdownStopsBothStreams()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "shutdown", timeout.Token);

        Task runtimeTermination = runtime.WaitForTerminationAsync(timeout.Token);
        Task workerTermination = worker.WaitForTerminationAsync(timeout.Token);
        await factory.DisposeAsync();
        await Task.WhenAll(runtimeTermination, workerTermination);

        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        Assert.Null(relay.LastTerminalState?.Side);
    }

    [Fact]
    public async Task Relay_ConcurrentDisposalIsIdempotent()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "dispose", timeout.Token);

        Task[] disposalTasks = Enumerable.Range(0, 16).Select(_ => relay.DisposeAsync().AsTask()).ToArray();
        await Task.WhenAll(disposalTasks).WaitAsync(timeout.Token);
        await Task.WhenAll(runtime.WaitForTerminationAsync(timeout.Token), worker.WaitForTerminationAsync(timeout.Token));

        Assert.All(disposalTasks, static task => Assert.True(task.IsCompletedSuccessfully));
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
    }

    [Fact]
    public async Task Relay_ConcurrentStopsJoinSharedCompletion()
    {
        FunctionRpcRelay relay = CreateInProcessRelay();
        using CancellationTokenSource timeout = new(TestTimeout);
        BlockingServerStreamWriter blockingWriter = new();

        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(FunctionRpcRelaySide.Runtime,
            new SingleMessageThenBlockStreamReader(CreateMessage("shared-stop")), new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker, new BlockingStreamReader(), blockingWriter, timeout.Token);
        await blockingWriter.WriteEntered.WaitAsync(timeout.Token);

        Task firstStop = relay.StopAsync(CancellationToken.None);
        Task secondStop = relay.StopAsync(CancellationToken.None);

        try
        {
            Assert.False(firstStop.IsCompleted);
            Assert.False(secondStop.IsCompleted);
        }
        finally
        {
            blockingWriter.Release();
        }

        await Task.WhenAll(firstStop, secondStop).WaitAsync(timeout.Token);
        FunctionRpcRelayTerminalState[] terminalStates =
            await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
        Assert.All(terminalStates, static state => Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, state.Reason));
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
    }

    [Fact]
    public async Task Relay_ShutdownAllowsSessionClearBeforeCancellation()
    {
        using BlockingLogger<FunctionRpcRelay> logger = new();
        FunctionRpcRelay relay = new(logger);
        using CancellationTokenSource timeout = new(TestTimeout);
        Task<FunctionRpcRelayTerminalState> runtimeTask =
            relay.AttachAsync(FunctionRpcRelaySide.Runtime, new BlockingStreamReader(), new TestServerStreamWriter(), timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask =
            relay.AttachAsync(FunctionRpcRelaySide.Worker, new BlockingStreamReader(), new TestServerStreamWriter(), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);

        Task stopTask = Task.Run(() => relay.StopAsync(timeout.Token), timeout.Token);
        try
        {
            await logger.LogEntered.WaitAsync(timeout.Token);
            FunctionRpcRelayTerminalState[] terminalStates =
                await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);
            Assert.All(terminalStates, static state => Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, state.Reason));
        }
        finally
        {
            logger.Release();
        }

        await stopTask.WaitAsync(timeout.Token);
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        await relay.DisposeAsync();
    }

    private static WorkerProxyWebApplicationFactory CreateFactory()
    {
        return new WorkerProxyWebApplicationFactory();
    }

    private static FunctionRpcRelay CreateInProcessRelay()
    {
        return new FunctionRpcRelay(NullLogger<FunctionRpcRelay>.Instance);
    }

    private static RelayClient CreateClient(WorkerProxyWebApplicationFactory factory, FunctionRpcRelaySide side, CancellationToken cancellationToken)
    {
        return CreateClient(factory.GetFunctionRpcAddress(side), cancellationToken);
    }

    private static RelayClient CreateClient(Uri address, CancellationToken cancellationToken)
    {
        UriBuilder normalizedAddress = new(address);
        if (IPAddress.TryParse(address.Host.Trim('[', ']'), out IPAddress? ipAddress)
            && (ipAddress.Equals(IPAddress.Any) || ipAddress.Equals(IPAddress.IPv6Any)))
        {
            normalizedAddress.Host = IPAddress.Loopback.ToString();
        }

        GrpcChannel channel = GrpcChannel.ForAddress(normalizedAddress.Uri);
        AsyncDuplexStreamingCall<StreamingMessage, StreamingMessage> call =
            channel.CreateCallInvoker().AsyncDuplexStreamingCall(EventStreamMethod, host: null,
                new CallOptions(cancellationToken: cancellationToken));

        return new RelayClient(channel, call);
    }

    private static StreamingMessage CreateMessage(string requestId, string? payload = null)
    {
        return new StreamingMessage
        {
            RequestId = requestId,
            RpcLog = new RpcLog
            {
                Level = RpcLog.Types.Level.Information,
                Message = payload ?? requestId
            }
        };
    }

    private static IReadOnlyList<StreamingMessage> CreateMessages(string prefix, int count)
    {
        return Enumerable.Range(0, count)
            .Select(index => CreateMessage($"{prefix}-{index}"))
            .ToArray();
    }

    private static async Task ExchangeAsync(RelayClient runtime, RelayClient worker, string requestIdPrefix, CancellationToken cancellationToken)
    {
        StreamingMessage runtimeMessage = CreateMessage($"{requestIdPrefix}-runtime");
        StreamingMessage workerMessage = CreateMessage($"{requestIdPrefix}-worker");
        await Task.WhenAll(runtime.WriteAsync(runtimeMessage, cancellationToken), worker.WriteAsync(workerMessage, cancellationToken));

        Assert.Equal(runtimeMessage, await worker.ReadAsync(cancellationToken));
        Assert.Equal(workerMessage, await runtime.ReadAsync(cancellationToken));
    }

    private static async Task WaitForAttachmentAsync(FunctionRpcRelay relay, FunctionRpcRelaySide side, CancellationToken cancellationToken)
    {
        while (!relay.IsAttached(side))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }

    private static async Task WaitForReleaseAsync(FunctionRpcRelay relay, CancellationToken cancellationToken)
    {
        while (relay.IsAttached(FunctionRpcRelaySide.Runtime) || relay.IsAttached(FunctionRpcRelaySide.Worker) || relay.LastTerminalState is null)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
