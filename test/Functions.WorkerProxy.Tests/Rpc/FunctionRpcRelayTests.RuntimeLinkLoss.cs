// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task RuntimeEventStream_EofBeforeWorkerInitializationRequestsRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerLinkState.Unlinked, removed.Link.State);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RuntimeEventStream_CancellationReadOrWriteFaultRequestsRemoval(int outcome)
    {
        if (outcome == 2)
        {
            await AssertRuntimeWriteFaultRequestsRemovalAsync();
            return;
        }

        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateRuntimeTerminalClient(factory, outcome, runtimeCancellation.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        switch (outcome)
        {
            case 0:
                runtimeCancellation.Cancel();
                break;
            case 1:
                await runtime.WriteAsync(CreateMessage("malformed-request"), timeout.Token);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown Runtime terminal outcome.");
        }

        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await WaitForRemovalStateAsync(management, timeout.Token)).Status);
        await WaitForReleaseAsync(relay, timeout.Token);
        if (outcome == 1)
        {
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, relay.LastTerminalState?.Reason);
            Assert.Equal(FunctionRpcRelaySide.Runtime, relay.LastTerminalState?.Side);
        }
    }

    [Fact]
    public async Task RejectedOrRelayCanceledRuntimeHandler_DoesNotRequestRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.WriteAsync(CreateMessage("admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        await using (RelayClient duplicate = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token))
        {
            Grpc.Core.RpcException rejected =
                await duplicate.WriteAndReadRejectionAsync(CreateMessage("duplicate"), timeout.Token);
            Assert.Equal(StatusCode.AlreadyExists, rejected.StatusCode);
            Assert.Equal(
                SplitWorkerStatus.Starting,
                (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        }

        await using (RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token))
        {
            StreamingMessage start = CreateStartStream("worker-closes-first");
            await worker.WriteAsync(start, timeout.Token);
            Assert.Equal(CreateMessage("admitted"), await worker.ReadAsync(timeout.Token));
            Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
            await worker.CompleteRequestAsync(timeout.Token);
            await Task.WhenAll(
                worker.WaitForTerminationAsync(timeout.Token),
                runtime.WaitForTerminationAsync(timeout.Token));
        }

        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.Equal(
            SplitWorkerStatus.Starting,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);

        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(replacementRuntime, replacementWorker, "replacement", timeout.Token);
    }

    [Fact]
    public async Task ApplicationShutdown_DoesNotRequestRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);

        await relay.StopAsync(timeout.Token);

        Assert.Contains(
            await runtime.WaitForTerminationAsync(timeout.Token),
            new[] { StatusCode.Internal, StatusCode.Unavailable, StatusCode.Cancelled });
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        Assert.Equal(
            SplitWorkerStatus.Starting,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
    }

    [Fact]
    public async Task RemovalRequested_IsStickyPollableAndClosesNewAdmission()
    {
        int forwardedRequests = 0;
        await using var destination = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref forwardedRequests);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                WorkerHttpForwardingTests.GetAddress(destination).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient http = factory.CreateHttpForwardingClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        await InitializeAndAssignWorkerAsync(worker, runtime, management, timeout.Token);

        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        await runtime.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);

        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        Assert.True(manager.State.RemovalRequested);
        Assert.Null(manager.State.SessionId);
        Assert.Null(manager.State.WorkerId);
        Assert.Null(manager.State.FunctionAppName);
        Assert.Null(manager.State.FunctionGroupName);
        Assert.Null(manager.State.StartupMode);
        Assert.Null(manager.State.IsAlwaysReady);
        Assert.False(manager.State.IsWorkerAttached);
        Assert.False(manager.State.IsWorkerReady);

        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        }

        using (HttpResponseMessage after = await http.GetAsync("/after-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, after.StatusCode);
        }

        Assert.Equal(1, Volatile.Read(ref forwardedRequests));
        foreach (FunctionRpcRelaySide side in new[] { FunctionRpcRelaySide.Runtime, FunctionRpcRelaySide.Worker })
        {
            await using RelayClient replacement = CreateClient(factory, side, timeout.Token);
            Grpc.Core.RpcException rejected = await replacement.WriteAndReadRejectionAsync(
                side == FunctionRpcRelaySide.Worker ? CreateStartStream("replacement") : CreateMessage("replacement"),
                timeout.Token);
            Assert.Equal(StatusCode.Unavailable, rejected.StatusCode);
        }
    }

    private static RelayClient CreateRuntimeTerminalClient(
        WorkerProxyWebApplicationFactory factory,
        int outcome,
        CancellationToken cancellationToken)
    {
        Marshaller<StreamingMessage> requestMarshaller = outcome == 1
            ? Marshallers.Create<StreamingMessage>(
                static message => string.Equals(message.RequestId, "malformed-request", StringComparison.Ordinal)
                    ? [0x80]
                    : message.ToByteArray(),
                static payload => StreamingMessage.Parser.ParseFrom(payload))
            : StreamingMessageMarshaller;
        Method<StreamingMessage, StreamingMessage> method = new(
            MethodType.DuplexStreaming,
            FunctionRpcServiceName,
            EventStreamMethodName,
            requestMarshaller,
            StreamingMessageMarshaller);
        return CreateClient(factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), method, cancellationToken);
    }

    private static async Task AssertRuntimeWriteFaultRequestsRemovalAsync()
    {
        WorkerPodStateManager manager = CreatePodStateManager();
        await using FunctionRpcRelay relay = CreateInProcessRelay(manager);
        using CancellationTokenSource timeout = new(TestTimeout);
        IOException expectedException = new("Injected Runtime write failure.");
        Mock<IServerStreamWriter<StreamingMessage>> runtimeWriter = new(MockBehavior.Strict);
        runtimeWriter
            .Setup(writer => writer.WriteAsync(It.IsAny<StreamingMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(expectedException);

        Task<FunctionRpcRelayTerminalState> runtimeTask = relay.AttachAsync(
            FunctionRpcRelaySide.Runtime,
            new BlockingStreamReader(),
            runtimeWriter.Object,
            timeout.Token);
        Task<FunctionRpcRelayTerminalState> workerTask = relay.AttachAsync(
            FunctionRpcRelaySide.Worker,
            new SingleMessageThenBlockStreamReader(CreateStartStream("write-fault")),
            new TestServerStreamWriter(),
            timeout.Token);

        FunctionRpcRelayTerminalState[] terminalStates =
            await Task.WhenAll(runtimeTask, workerTask).WaitAsync(timeout.Token);

        Assert.True(manager.State.RemovalRequested);
        Assert.All(terminalStates, state => Assert.Same(expectedException, state.Exception));
        Assert.All(terminalStates, state => Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, state.Reason));
        Assert.All(terminalStates, state => Assert.Equal(FunctionRpcRelaySide.Runtime, state.Side));
    }

    private static async Task InitializeAndAssignWorkerAsync(
        RelayClient worker,
        RelayClient runtime,
        HttpClient management,
        CancellationToken cancellationToken)
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
        await AssignWorkerAsync(management, cancellationToken);

        StreamingMessage request = new()
        {
            RequestId = "worker-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(request, cancellationToken);
        Assert.Equal(request, await worker.ReadAsync(cancellationToken));
        await worker.WriteAsync(CreateInitResponse(request.RequestId, httpUri: null), cancellationToken);
        Assert.NotNull((await runtime.ReadAsync(cancellationToken)).WorkerInitResponse);
    }

    private static async Task<SplitWorkerOriginState> WaitForRemovalStateAsync(
        HttpClient management,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            SplitWorkerOriginState state = await ReadSplitManagementStateAsync(management, cancellationToken);
            if (state.Status == SplitWorkerStatus.RemovalRequested)
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
