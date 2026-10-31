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
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using GrpcRpcException = Grpc.Core.RpcException;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Fact]
    public async Task WorkerGrpc_PreEstablishmentTerminalOutcomesAllowReplacement()
    {
        string[] triggers = ["invalid-first", "prestart-eof", "prestart-cancel"];
        foreach (string trigger in triggers)
        {
            await AssertPreEstablishmentTerminalOutcomeAllowsReplacementAsync(trigger);
        }
    }

    [Fact]
    public async Task WorkerGrpc_EstablishedEofRequestsStickyRemovalAndClosesAdmissions()
    {
        await using WebApplication workerApp = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        Dictionary<string, string?> configuration = new()
        {
            [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.WorkerHttpEndpoint)}"] =
                WorkerHttpForwardingTests.GetAddress(workerApp).AbsoluteUri
        };
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "established-eof", timeout.Token);
        await AssignWorkerAsync(management, timeout.Token);
        using (HttpResponseMessage admitted = await forwarding.GetAsync("/invoke", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, admitted.StatusCode);
        }

        Task<GrpcRpcException> runtimeTermination = runtime.WaitForTerminationExceptionAsync(timeout.Token);
        await worker.CompleteRequestAsync(timeout.Token);

        GrpcRpcException runtimeException = await runtimeTermination;
        Assert.Equal(StatusCode.Unavailable, runtimeException.StatusCode);
        AssertRemovalTrailer(runtimeException);
        SplitWorkerOriginState first = await WaitForRemovalStateAsync(management, timeout.Token);
        using HttpResponseMessage repeatedResponse = await PollSplitManagementStateAsync(management, first, timeout.Token);
        SplitWorkerOriginState repeated = await ReadSplitManagementStateAsync(repeatedResponse, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, repeated.Status);
        Assert.Equal(first.RevisionId, repeated.RevisionId);
        Assert.Equal("test-group", repeated.FunctionGroup);
        Assert.True(repeated.IsUnhealthy);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        await AssertReadinessAsync(management, "/admin/instance/ready", HttpStatusCode.OK, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.Conflict, "RemovalRequested", timeout.Token);
        }

        using (HttpResponseMessage rejected = await forwarding.GetAsync("/invoke", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, rejected.StatusCode);
        }

        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Fact]
    public async Task WorkerGrpc_EstablishedCancellationRequestsStickyRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, workerCancellation.Token);
        await ExchangeAsync(runtime, worker, "established-cancel", timeout.Token);

        Task<GrpcRpcException> runtimeTermination = runtime.WaitForTerminationExceptionAsync(timeout.Token);
        workerCancellation.Cancel();

        GrpcRpcException runtimeException = await runtimeTermination;
        Assert.Equal(StatusCode.Cancelled, runtimeException.StatusCode);
        AssertRemovalTrailer(runtimeException);
        SplitWorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.RemovalRequested, removed.Status);
        Assert.True(removed.IsUnhealthy);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Fact]
    public async Task WorkerGrpc_ApplicationShutdownDoesNotPublishRemoval()
    {
        WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "shutdown", timeout.Token);
        WorkerPodState beforeShutdown = manager.State;

        await factory.DisposeAsync();

        Assert.False(manager.State.RemovalRequested);
        Assert.Equal(beforeShutdown.WorkerId, manager.State.WorkerId);
    }

    private static async Task AssertPreEstablishmentTerminalOutcomeAllowsReplacementAsync(string trigger)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using HttpClient management = factory.CreateWorkerProxyClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource workerCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, workerCancellation.Token);
        await runtime.WriteAsync(CreateMessage($"{trigger}-runtime"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        Task<GrpcRpcException> runtimeTermination = runtime.WaitForTerminationExceptionAsync(timeout.Token);

        switch (trigger)
        {
            case "invalid-first":
                await worker.WriteAsync(CreateMessage("invalid-first"), timeout.Token);
                break;
            case "prestart-eof":
                await worker.CompleteRequestAsync(timeout.Token);
                break;
            case "prestart-cancel":
                workerCancellation.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        GrpcRpcException runtimeException = await runtimeTermination;
        AssertNoRemovalTrailer(runtimeException);
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.False(manager.State.RemovalRequested);
        SplitWorkerOriginState state = await ReadSplitManagementStateAsync(management, timeout.Token);
        Assert.Equal(SplitWorkerStatus.Starting, state.Status);
        Assert.False(state.IsUnhealthy);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.ServiceUnavailable, timeout.Token);
        using (HttpResponseMessage assignment = await PutManagementJsonAsync(
            management, "/admin/worker/assignment", ManagementAssignment, timeout.Token))
        {
            await AssertManagementErrorAsync(assignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
        }

        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(replacementRuntime, replacementWorker, $"{trigger}-replacement", timeout.Token);
        await AssertReadinessAsync(management, "/admin/worker/ready", HttpStatusCode.OK, timeout.Token);
    }

    private static async Task AssertLateStreamRejectedAsync(
        WorkerProxyWebApplicationFactory factory,
        FunctionRpcRelaySide side,
        CancellationToken cancellationToken)
    {
        await using RelayClient client = CreateClient(factory, side, cancellationToken);
        StreamingMessage message = side == FunctionRpcRelaySide.Worker
            ? CreateStartStream("late-worker")
            : CreateMessage("late-runtime");
        GrpcRpcException exception = await client.WriteAndReadRejectionAsync(message, cancellationToken);
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
    }

    private static void AssertRemovalTrailer(GrpcRpcException exception)
    {
        Metadata.Entry trailer = Assert.Single(exception.Trailers, entry =>
            string.Equals(entry.Key, FunctionRpcRelayConstants.WorkerRemovalRequestedTrailerName, StringComparison.Ordinal));
        Assert.Equal(FunctionRpcRelayConstants.WorkerRemovalRequestedTrailerValue, trailer.Value);
    }

    private static void AssertNoRemovalTrailer(GrpcRpcException exception)
    {
        Assert.DoesNotContain(exception.Trailers, entry =>
            string.Equals(entry.Key, FunctionRpcRelayConstants.WorkerRemovalRequestedTrailerName, StringComparison.Ordinal));
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
