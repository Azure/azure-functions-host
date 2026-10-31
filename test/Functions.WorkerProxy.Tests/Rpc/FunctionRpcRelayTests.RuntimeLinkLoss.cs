// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string HandlesWorkerTerminateMessageCapability = "HandlesWorkerTerminateMessage";
    private const int WorkerTerminateGracePeriodInSeconds = 5;

    [Fact]
    public async Task RuntimeLinkLoss_CapableWorkerReceivesTerminateAndHistoricalAttributionIsRetained()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        await AcceptRuntimeLinkAsync(worker, runtime, management, supportsWorkerTerminate: true, timeout.Token);
        WorkerPodState accepted = manager.State;

        await runtime.CompleteRequestAsync(timeout.Token);

        StreamingMessage terminate = await worker.ReadAsync(timeout.Token);
        Assert.Equal(
            Duration.FromTimeSpan(TimeSpan.FromSeconds(WorkerTerminateGracePeriodInSeconds)),
            terminate.WorkerTerminate.GracePeriod);
        Task<StatusCode> workerTermination = worker.WaitForTerminationAsync(timeout.Token);
        Assert.False(workerTermination.IsCompleted);
        await worker.CompleteRequestAsync(timeout.Token);
        Assert.Equal(StatusCode.Unavailable, await workerTermination);
        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(factory.Services.GetRequiredService<FunctionRpcRelay>(), timeout.Token);

        WorkerPodState removed = manager.State;
        Assert.True(removed.RemovalRequested);
        Assert.False(removed.IsWorkerAttached);
        Assert.False(removed.IsWorkerReady);
        Assert.Equal(accepted.SessionId, removed.SessionId);
        Assert.Equal(accepted.WorkerId, removed.WorkerId);
        Assert.Equal(accepted.FunctionAppName, removed.FunctionAppName);
        Assert.Equal(accepted.FunctionGroupName, removed.FunctionGroupName);
        Assert.Equal(accepted.SessionId, manager.LastAcceptedRuntimeSessionId);
        Assert.Equal(accepted.Revision + 1, removed.Revision);
        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
    }

    [Fact]
    public async Task RuntimeLinkLoss_UnsupportedWorkerRemovesWithoutTerminate()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await AcceptRuntimeLinkAsync(worker, runtime, management, supportsWorkerTerminate: false, timeout.Token);

        await runtime.CompleteRequestAsync(timeout.Token);

        (IReadOnlyList<StreamingMessage> messages, StatusCode workerStatus) =
            await worker.ReadUntilTerminationAsync(timeout.Token);
        Assert.Empty(messages);
        Assert.Equal(StatusCode.Unavailable, workerStatus);
        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(
            SplitWorkerStatus.RemovalRequested,
            (await WaitForRemovalStateAsync(management, timeout.Token)).Status);
    }

    [Fact]
    public async Task RuntimeLinkLoss_CancellationAndFaultPublishRemovalBeforeNotificationConsumption()
    {
        foreach (bool faultRuntime in new[] { false, true })
        {
            await using WorkerProxyWebApplicationFactory factory = CreateFactory();
            using CancellationTokenSource timeout = new(TestTimeout);
            using CancellationTokenSource runtimeCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            using HttpClient management = factory.CreateWorkerProxyClient();
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient runtime = faultRuntime
                ? CreateFaultableClient(factory, FunctionRpcRelaySide.Runtime, runtimeCancellation.Token)
                : CreateClient(factory, FunctionRpcRelaySide.Runtime, runtimeCancellation.Token);
            await AcceptRuntimeLinkAsync(worker, runtime, management, supportsWorkerTerminate: true, timeout.Token);

            if (faultRuntime)
            {
                await runtime.WriteAsync(CreateMessage("malformed-frame"), timeout.Token);
            }
            else
            {
                runtimeCancellation.Cancel();
            }

            SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
            Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
            Assert.NotNull((await worker.ReadAsync(timeout.Token)).WorkerTerminate);
            Task<StatusCode> workerTermination = worker.WaitForTerminationAsync(timeout.Token);
            Assert.False(workerTermination.IsCompleted);
            await worker.CompleteRequestAsync(timeout.Token);
            Assert.Equal(faultRuntime ? StatusCode.Unavailable : StatusCode.Cancelled, await workerTermination);
        }
    }

    [Fact]
    public async Task RemovalRequested_ClosesGrpcAssignmentHttpAndWorkerReadinessAdmission()
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
        await AcceptRuntimeLinkAsync(worker, runtime, management, supportsWorkerTerminate: false, timeout.Token);
        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        await runtime.CompleteRequestAsync(timeout.Token);
        await worker.WaitForTerminationAsync(timeout.Token);
        await runtime.WaitForTerminationAsync(timeout.Token);
        await WaitForRemovalStateAsync(management, timeout.Token);

        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.Conflict, "RemovalRequested", timeout.Token);
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

    [Fact]
    public async Task NonW2Termination_PreAcceptedRuntimeLossAndApplicationShutdownDoNotRequestRemoval()
    {
        await using (WorkerProxyWebApplicationFactory factory = CreateFactory())
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            using HttpClient management = factory.CreateWorkerProxyClient();
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await ExchangeAsync(runtime, worker, "before-link-acceptance", timeout.Token);

            await runtime.CompleteRequestAsync(timeout.Token);
            await worker.WaitForTerminationAsync(timeout.Token);
            await runtime.WaitForTerminationAsync(timeout.Token);
            Assert.Equal(
                SplitWorkerStatus.Starting,
                (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);

            await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await ExchangeAsync(replacementRuntime, replacementWorker, "replacement-before-acceptance", timeout.Token);
        }

        await using (WorkerProxyWebApplicationFactory factory = CreateFactory())
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            using HttpClient management = factory.CreateWorkerProxyClient();
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            StreamingMessage start = CreateStartStream("acceptance-race");
            await worker.WriteAsync(start, timeout.Token);
            Assert.Equal(start, await runtime.ReadAsync(timeout.Token));
            await AssignWorkerAsync(management, timeout.Token);
            StreamingMessage request = new()
            {
                RequestId = "acceptance-race",
                WorkerInitRequest = new() { HostVersion = "test-host" }
            };
            await runtime.WriteAsync(request, timeout.Token);
            Assert.Equal(request, await worker.ReadAsync(timeout.Token));

            await runtime.CompleteRequestAsync(timeout.Token);
            try
            {
                await worker.WriteAsync(CreateInitResponse(request.RequestId, httpUri: null), timeout.Token);
            }
            catch (Grpc.Core.RpcException exception) when (
                exception.StatusCode is StatusCode.Unavailable or StatusCode.Cancelled)
            {
            }

            await worker.WaitForTerminationAsync(timeout.Token);
            await runtime.WaitForTerminationAsync(timeout.Token);
            WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
            Assert.False(manager.State.RemovalRequested);
            Assert.Null(manager.LastAcceptedRuntimeSessionId);
            Assert.Equal(
                SplitWorkerStatus.Starting,
                (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        }

        await using (WorkerProxyWebApplicationFactory factory = CreateFactory())
        {
            using CancellationTokenSource timeout = new(TestTimeout);
            using HttpClient management = factory.CreateWorkerProxyClient();
            await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await AcceptRuntimeLinkAsync(worker, runtime, management, supportsWorkerTerminate: true, timeout.Token);
            FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

            await relay.StopAsync(timeout.Token);

            (IReadOnlyList<StreamingMessage> messages, StatusCode workerStatus) =
                await worker.ReadUntilTerminationAsync(timeout.Token);
            Assert.Empty(messages);
            Assert.Contains(workerStatus, new[] { StatusCode.Internal, StatusCode.Unavailable, StatusCode.Cancelled });
            Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
            Assert.False(factory.Services.GetRequiredService<WorkerPodStateManager>().State.RemovalRequested);
            Assert.Equal(
                SplitWorkerStatus.Starting,
                (await ReadSplitManagementStateAsync(management, timeout.Token)).Status);
        }
    }

    private static async Task AcceptRuntimeLinkAsync(
        RelayClient worker,
        RelayClient runtime,
        HttpClient management,
        bool supportsWorkerTerminate,
        CancellationToken cancellationToken)
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
        await AssignWorkerAsync(management, cancellationToken);

        StreamingMessage request = new()
        {
            RequestId = "runtime-link",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(request, cancellationToken);
        Assert.Equal(request, await worker.ReadAsync(cancellationToken));

        StreamingMessage response = CreateInitResponse(request.RequestId, httpUri: null);
        if (supportsWorkerTerminate)
        {
            response.WorkerInitResponse.Capabilities[HandlesWorkerTerminateMessageCapability] = "1";
        }

        await worker.WriteAsync(response, cancellationToken);
        StreamingMessage accepted = await runtime.ReadAsync(cancellationToken);
        Assert.Equal(
            supportsWorkerTerminate,
            accepted.WorkerInitResponse.Capabilities.ContainsKey(HandlesWorkerTerminateMessageCapability));
    }

    private static RelayClient CreateFaultableClient(
        WorkerProxyWebApplicationFactory factory,
        FunctionRpcRelaySide side,
        CancellationToken cancellationToken)
    {
        Marshaller<StreamingMessage> marshaller = Marshallers.Create(
            static message => string.Equals(message.RequestId, "malformed-frame", StringComparison.Ordinal)
                ? [0x80]
                : message.ToByteArray(),
            static payload => StreamingMessage.Parser.ParseFrom(payload));
        Method<StreamingMessage, StreamingMessage> method = new(
            MethodType.DuplexStreaming,
            FunctionRpcServiceName,
            EventStreamMethodName,
            marshaller,
            StreamingMessageMarshaller);
        return CreateClient(factory.GetFunctionRpcAddress(side), method, cancellationToken);
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
