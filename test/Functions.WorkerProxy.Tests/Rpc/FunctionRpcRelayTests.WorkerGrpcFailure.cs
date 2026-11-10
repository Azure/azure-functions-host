// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using Xunit;

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

    [Theory]
    [InlineData("Preconfigured", null, 16, false)]
    [InlineData("Preconfigured", 10, 10, false)]
    [InlineData("SpecializationRequired", null, 16, false)]
    [InlineData("SpecializationRequired", 10, 10, false)]
    [InlineData("Preconfigured", null, 16, true)]
    [InlineData("Preconfigured", 10, 10, true)]
    [InlineData("SpecializationRequired", null, 16, true)]
    [InlineData("SpecializationRequired", 10, 10, true)]
    public async Task WorkerGrpc_EstablishedEofRequestsStickyRemovalAndClosesAdmissions(
        string startupMode, int? maxHttpConcurrency, int expectedConcurrency, bool drainBeforeDisconnect)
    {
        int workerRequestCount = 0;
        await using WebApplication workerApp = await WorkerHttpForwardingTests.StartWorkerAsync(context =>
        {
            Interlocked.Increment(ref workerRequestCount);
            context.Response.StatusCode = StatusCodes.Status204NoContent;
            return Task.CompletedTask;
        });
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using HttpClient management = factory.CreateWorkerProxyClient();
        using HttpClient forwarding = factory.CreateHttpForwardingClient();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        WorkerAssignRequest assignment = new()
        {
            StartupMode = startupMode,
            FunctionAppName = "test-app",
            FunctionGroupName = "http",
            HttpProxyEndpoint = forwarding.BaseAddress!.Authority,
            IsAlwaysReady = false,
            ConfigLastModifiedTime = 123456,
            ContentLastModifiedTime = 234567,
            Environment = [],
            FunctionAppDirectory = "/home/site/wwwroot"
        };
        if (maxHttpConcurrency is { } concurrency)
        {
            assignment.MaxHttpConcurrency = concurrency;
        }

        StreamingMessage initialized = await InitializeAndAssignWorkerAsync(
            worker, runtime, management, assignment,
            WorkerHttpForwardingTests.GetAddress(workerApp).AbsoluteUri, timeout.Token);
        Assert.Equal(forwarding.BaseAddress.AbsoluteUri, initialized.WorkerInitResponse.Capabilities["HttpUri"]);
        Assert.Equal("http", initialized.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        Assert.Equal(
            expectedConcurrency.ToString(CultureInfo.InvariantCulture),
            initialized.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability]);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        WorkerPodState assignedState = manager.State;
        Assert.Equal(expectedConcurrency, assignedState.MaxHttpConcurrency);
        Assert.Equal(assignment.ConfigLastModifiedTime, assignedState.ConfigLastModifiedTime);
        Assert.Equal(assignment.ContentLastModifiedTime, assignedState.ContentLastModifiedTime);
        using (HttpResponseMessage beforeRemoval = await forwarding.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, beforeRemoval.StatusCode);
            Assert.Equal(1, workerRequestCount);
        }

        if (drainBeforeDisconnect)
        {
            await runtime.WriteAsync(new() { WorkerDraining = new() }, timeout.Token);
            while (!manager.State.IsDraining)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            Assert.Equal(WorkerPodStatus.Draining, manager.State.PodStatus);
        }

        await worker.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        WorkerOriginState first = await WaitForRemovalStateAsync(management, timeout.Token);
        using HttpResponseMessage repeatedResponse = await PollManagementStateAsync(management, first, timeout.Token);
        WorkerOriginState repeated = await ReadManagementStateAsync(repeatedResponse, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, repeated.Status);
        Assert.Equal(first.RevisionId, repeated.RevisionId);
        Assert.Equal(!drainBeforeDisconnect, repeated.IsUnhealthy);
        Assert.Equal(assignment.ConfigLastModifiedTime, repeated.ConfigLastModifiedTime);
        Assert.Equal(assignment.ContentLastModifiedTime, repeated.ContentLastModifiedTime);
        WorkerPodState removedState = manager.State;
        Assert.Equal(drainBeforeDisconnect ? WorkerAssignmentState.Terminated : WorkerAssignmentState.Failed,
            removedState.AssignmentState);
        Assert.False(removedState.IsWorkerReady);
        Assert.Equal(assignedState.FunctionAppName, removedState.FunctionAppName);
        Assert.Equal(assignedState.FunctionGroupName, removedState.FunctionGroupName);
        Assert.Equal(expectedConcurrency, removedState.MaxHttpConcurrency);
        Assert.Equal(assignedState.ConfigLastModifiedTime, removedState.ConfigLastModifiedTime);
        Assert.Equal(assignedState.ContentLastModifiedTime, removedState.ContentLastModifiedTime);
        Assert.Equal(assignedState.StartupTime, removedState.StartupTime);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(assignedState.SessionId!.Value).HttpProxyEndpoint);
        Assert.Null(factory.Services.GetRequiredService<FunctionRpcRelay>().WorkerHttpDestination);
        using (HttpResponseMessage reassignment = await management.PutAsJsonAsync(
            "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, timeout.Token))
        {
            await AssertManagementErrorAsync(reassignment, HttpStatusCode.Conflict, "WorkerTerminated", timeout.Token);
        }

        using (HttpResponseMessage afterRemoval = await forwarding.GetAsync("/after-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, afterRemoval.StatusCode);
            Assert.Equal(1, workerRequestCount);
        }

        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "PeerReleased", StringComparison.Ordinal)) == 2, timeout.Token);
        CapturedProxyLog terminated = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "SessionTerminated", StringComparison.Ordinal));
        Assert.Equal(assignedState.WorkerId, terminated.Context!.WorkerId);
        Assert.Equal(assignedState.SessionId, terminated.Context.ProxySessionId);
        await WaitForLogAsync(() => generator.ProxyRecords.Count(record =>
            string.Equals(record.EventName, "AttachmentRejected", StringComparison.Ordinal)) == 2, timeout.Token);
        Assert.Same(removedState, manager.State);
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

        workerCancellation.Cancel();

        Assert.Equal(StatusCode.Cancelled, await runtime.WaitForTerminationAsync(timeout.Token));
        WorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
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

        await factory.DisposeAsync();

        Assert.False(manager.State.RemovalRequested);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task WorkerGrpc_ApplicationStoppingBeforeRelayShutdownDoesNotPublishRemoval(
        bool runtimeCloses, bool cancelStream)
    {
        TaskCompletionSource<bool> stoppingEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> releaseStopping = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Mock<IHostedLifecycleService> stoppingService = new();
        stoppingService.Setup(service => service.StoppingAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken cancellationToken) =>
            {
                stoppingEntered.TrySetResult(true);
                await releaseStopping.Task.WaitAsync(cancellationToken);
            });
        // Hosted services stop in reverse registration order, keeping relay shutdown behind this gate.
        await using WorkerProxyWebApplicationFactory factory = new(
            configureServices: services => services.AddSingleton<IHostedService>(stoppingService.Object));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        IHostApplicationLifetime lifetime = factory.Services.GetRequiredService<IHostApplicationLifetime>();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource streamCancellation = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime,
            runtimeCloses ? streamCancellation.Token : timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker,
            runtimeCloses ? timeout.Token : streamCancellation.Token);
        await ExchangeAsync(runtime, worker, "stopping", timeout.Token);

        TaskCompletionSource<bool> stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        using CancellationTokenRegistration registration = lifetime.ApplicationStopped.Register(() => stopped.TrySetResult(true));
        lifetime.StopApplication();
        try
        {
            await stoppingEntered.Task.WaitAsync(timeout.Token);
            Assert.True(lifetime.ApplicationStopping.IsCancellationRequested);
            await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
            await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
            if (cancelStream)
            {
                streamCancellation.Cancel();
            }
            else
            {
                RelayClient closingPeer = runtimeCloses ? runtime : worker;
                await closingPeer.CompleteRequestAsync(timeout.Token);
            }

            StatusCode[] terminalStatuses = await Task.WhenAll(
                runtime.WaitForTerminationAsync(timeout.Token),
                worker.WaitForTerminationAsync(timeout.Token));
            Assert.All(terminalStatuses, status =>
                Assert.Equal(cancelStream ? StatusCode.Cancelled : StatusCode.Unavailable, status));
            await WaitForReleaseAsync(relay, timeout.Token);
            Assert.False(manager.State.RemovalRequested);
            Assert.False(manager.State.IsWorkerReady);
        }
        finally
        {
            releaseStopping.TrySetResult(true);
            await stopped.Task.WaitAsync(timeout.Token);
        }
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
        switch (trigger)
        {
            case "invalid-first":
                await worker.WriteAsync(CreateMessage("invalid-first"), timeout.Token);
                break;
            case "prestart-eof":
                await worker.CompleteRequestAsync(timeout.Token);
                break;
            case "prestart-cancel":
                await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Worker, timeout.Token);
                Assert.Null(manager.State.WorkerId);
                workerCancellation.Cancel();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(trigger));
        }

        StatusCode[] terminalStatuses = await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        Assert.All(terminalStatuses, status => Assert.NotEqual(StatusCode.OK, status));
        await WaitForReleaseAsync(relay, timeout.Token);
        Assert.False(manager.State.RemovalRequested);
        WorkerOriginState state = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Starting, state.Status);
        Assert.False(state.IsUnhealthy);
        await AssignWorkerAsync(management, timeout.Token);

        await using RelayClient replacementRuntime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient replacementWorker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(replacementRuntime, replacementWorker, $"{trigger}-replacement", timeout.Token);
        Assert.True(manager.State.IsWorkerReady);
    }

    private static async Task AssertLateStreamRejectedAsync(
        WorkerProxyWebApplicationFactory factory,
        FunctionRpcRelaySide side,
        CancellationToken cancellationToken)
    {
        await using RelayClient client = CreateClient(factory, side, cancellationToken);
        Assert.Equal(StatusCode.Unavailable, await client.WaitForTerminationAsync(cancellationToken));
    }
}
