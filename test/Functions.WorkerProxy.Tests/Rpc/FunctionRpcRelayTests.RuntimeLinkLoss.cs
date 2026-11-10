// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AttachmentRejected_DistinguishesRemovalFromShutdown(bool shutdown, bool runtimeSide)
    {
        CapturingEventGenerator generator = new();
        await using WorkerProxyWebApplicationFactory factory = CreateSystemLoggingFactory(generator);
        using CancellationTokenSource timeout = new(TestTimeout);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        if (shutdown)
        {
            await relay.StopAsync(timeout.Token);
        }
        else
        {
            Assert.True(factory.Services.GetRequiredService<WorkerPodStateManager>().RequestRemoval());
        }

        FunctionRpcRelaySide side = runtimeSide ? FunctionRpcRelaySide.Runtime : FunctionRpcRelaySide.Worker;
        await using RelayClient replacement = CreateClient(factory, side, timeout.Token);
        Grpc.Core.RpcException rejected = await replacement.WriteAndReadRejectionAsync(
            runtimeSide ? CreateMessage("replacement") : CreateStartStream("replacement"), timeout.Token);
        Assert.Equal(StatusCode.Unavailable, rejected.StatusCode);
        await WaitForLogAsync(() => generator.ProxyRecords.Any(record =>
            string.Equals(record.EventName, "AttachmentRejected", StringComparison.Ordinal)), timeout.Token);
        CapturedProxyLog rejection = Assert.Single(generator.ProxyRecords, record =>
            string.Equals(record.EventName, "AttachmentRejected", StringComparison.Ordinal));
        Assert.Contains(
            shutdown ? nameof(FunctionRpcRelayAttachmentFailure.Shutdown) : nameof(FunctionRpcRelayAttachmentFailure.PodStateRejected),
            rejection.Message);
        Assert.Contains(side.ToString(), rejection.Message);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RuntimeEventStream_EofBeforeWorkerInitializationRequestsRemoval(bool attachWorker, bool sendProtocolInput)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await using RelayClient? worker = attachWorker ? CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token) : null;
        if (worker is not null)
        {
            await worker.WriteAsync(CreateStartStream(), timeout.Token);
            Assert.NotNull((await runtime.ReadAsync(timeout.Token)).StartStream);
        }

        if (sendProtocolInput)
        {
            await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        }

        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);

        Assert.Equal(StatusCode.Unavailable, await runtime.WaitForTerminationAsync(timeout.Token));
        WorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(WorkerLinkState.Unlinked, removed.Link.State);
    }

    [Fact]
    public async Task RuntimeEventStream_CancellationRequestsRemoval()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, runtimeCancellation.Token);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();

        await runtime.WriteAsync(CreateMessage("runtime-admitted"), timeout.Token);
        await WaitForAttachmentAsync(relay, FunctionRpcRelaySide.Runtime, timeout.Token);
        runtimeCancellation.Cancel();

        Assert.Equal(
            WorkerStatus.RemovalRequested,
            (await WaitForRemovalStateAsync(management, timeout.Token)).Status);
        await WaitForReleaseAsync(relay, timeout.Token);
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
        Assert.Equal(
            WorkerStatus.Starting,
            (await ReadManagementStateAsync(management, timeout.Token)).Status);
    }

    [Theory]
    [InlineData("Preconfigured", "RuntimeHalfClose", false, true)]
    [InlineData("SpecializationRequired", "RuntimeHalfClose", false, true)]
    [InlineData("Preconfigured", "WorkerFirst", false, true)]
    [InlineData("SpecializationRequired", "WorkerFirst", false, true)]
    [InlineData("Preconfigured", "RuntimeDisposed", false, true)]
    [InlineData("SpecializationRequired", "RuntimeDisposed", false, true)]
    [InlineData("Preconfigured", "RuntimeCanceled", false, true)]
    [InlineData("SpecializationRequired", "RuntimeCanceled", false, true)]
    [InlineData("Preconfigured", "WorkerCanceled", false, true)]
    [InlineData("SpecializationRequired", "WorkerCanceled", false, true)]
    [InlineData("Preconfigured", "WorkerFirst", true, true)]
    [InlineData("SpecializationRequired", "WorkerFirst", true, true)]
    [InlineData("Preconfigured", "RuntimeDisposed", true, true)]
    [InlineData("SpecializationRequired", "RuntimeDisposed", true, true)]
    [InlineData("Preconfigured", "RuntimeCanceled", true, true)]
    [InlineData("SpecializationRequired", "RuntimeCanceled", true, true)]
    [InlineData("Preconfigured", "WorkerProtocolFault", false, true)]
    [InlineData("SpecializationRequired", "WorkerProtocolFault", false, true)]
    [InlineData("Preconfigured", "RuntimeHalfClose", false, false)]
    [InlineData("SpecializationRequired", "RuntimeHalfClose", false, false)]
    [InlineData("Preconfigured", "RuntimeDisposed", false, false)]
    [InlineData("SpecializationRequired", "RuntimeDisposed", false, false)]
    [InlineData("Preconfigured", "RuntimeCanceled", false, false)]
    [InlineData("SpecializationRequired", "RuntimeCanceled", false, false)]
    [InlineData("Preconfigured", "RuntimeCanceled", true, false)]
    [InlineData("SpecializationRequired", "RuntimeCanceled", true, false)]
    [InlineData("Preconfigured", "WorkerFirst", false, false)]
    [InlineData("SpecializationRequired", "WorkerFirst", false, false)]
    [InlineData("Preconfigured", "WorkerCanceled", false, false)]
    [InlineData("SpecializationRequired", "WorkerCanceled", false, false)]
    public async Task TerminalStream_DrainAndFailureStateDetermineHealth(
        string startupMode, string retirementPath, bool hasAuthenticationError, bool drainBeforeRetirement)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        using CancellationTokenSource timeout = new(TestTimeout);
        using CancellationTokenSource runtimeCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using CancellationTokenSource workerCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
        using HttpClient management = factory.CreateWorkerProxyClient();
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, workerCancellation.Token);
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime),
            runtimeCancellation.Token,
            CreateRuntimeIdentityHeaders("valid"));
        WorkerAssignRequest assignment = new()
        {
            StartupMode = startupMode,
            FunctionAppName = "test-app",
            FunctionGroupName = "http",
            HttpProxyEndpoint = "worker-pod:28080",
            IsAlwaysReady = false,
            Environment = [],
            FunctionAppDirectory = "/home/site/wwwroot",
            ConfigLastModifiedTime = 111,
            ContentLastModifiedTime = 222
        };
        await InitializeAndAssignWorkerAsync(
            worker, runtime, management, assignment, "http://localhost:1234/", timeout.Token);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        while (!manager.State.IsRuntimeInitialized)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        await using var transport = runtime.CreateRuntimeTransport();
        manager.SetAuthenticationConfigurationError(hasAuthenticationError);
        WorkerOriginState running = await ReadManagementStateAsync(management, timeout.Token);
        WorkerOriginState beforeRetirement = running;
        if (drainBeforeRetirement)
        {
            Task<HttpResponseMessage> drainPoll = PollManagementStateAsync(management, running, timeout.Token);
            await WaitForManagementPollAsync(manager, timeout.Token);
            await transport.Writer.WriteAsync(new() { WorkerDraining = new() }, timeout.Token);
            using HttpResponseMessage drainResponse = await drainPoll;
            beforeRetirement = await ReadManagementStateAsync(drainResponse, timeout.Token);
            Assert.Equal(WorkerStatus.Draining, beforeRetirement.Status);
            Assert.Equal(hasAuthenticationError, beforeRetirement.IsUnhealthy);

            StreamingMessage terminate = new()
            {
                WorkerTerminate = new()
                {
                    GracePeriod = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan(TimeSpan.FromSeconds(5))
                }
            };
            await transport.Writer.WriteAsync(terminate, timeout.Token);
            Assert.Equal(terminate, await worker.ReadAsync(timeout.Token));
        }

        switch (retirementPath)
        {
            case "RuntimeHalfClose":
                transport.Writer.Complete();
                break;
            case "WorkerFirst":
                await worker.CompleteRequestAsync(timeout.Token);
                break;
            case "RuntimeDisposed":
                await transport.DisposeAsync();
                break;
            case "RuntimeCanceled":
                runtimeCancellation.Cancel();
                await transport.DisposeAsync();
                break;
            case "WorkerCanceled":
                workerCancellation.Cancel();
                break;
            case "WorkerProtocolFault":
                await worker.WriteAsync(new() { WorkerDraining = new() }, timeout.Token);
                break;
            default:
                throw new InvalidOperationException("Unknown retirement path.");
        }

        Exception? completionError = await Record.ExceptionAsync(
            () => transport.Reader.Completion.WaitAsync(timeout.Token));
        Assert.True(completionError is null or Grpc.Core.RpcException, completionError?.ToString());
        await worker.WaitForTerminationAsync(timeout.Token);
        await transport.DisposeAsync();
        WorkerOriginState removed = await WaitForRemovalStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
        bool unexpectedRuntimeExit = !drainBeforeRetirement &&
            retirementPath is "RuntimeHalfClose" or "RuntimeDisposed" or "RuntimeCanceled";
        bool unexpectedWorkerExit = !drainBeforeRetirement && retirementPath is "WorkerFirst" or "WorkerCanceled";
        Assert.Equal(hasAuthenticationError || unexpectedRuntimeExit || unexpectedWorkerExit ||
            string.Equals(retirementPath, "WorkerProtocolFault", StringComparison.Ordinal), removed.IsUnhealthy);
        Assert.Equal(WorkerLinkState.Unlinked, removed.Link.State);
        Assert.True(removed.RevisionId > beforeRetirement.RevisionId);
        Assert.Equal(running.FunctionGroup, removed.FunctionGroup);
        Assert.Equal(running.ConfigLastModifiedTime, removed.ConfigLastModifiedTime);
        Assert.Equal(running.ContentLastModifiedTime, removed.ContentLastModifiedTime);
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        await WaitForReleaseAsync(relay, timeout.Token);
        FunctionRpcRelayTerminalState terminal = Assert.IsType<FunctionRpcRelayTerminalState>(relay.LastTerminalState);
        if (retirementPath is "WorkerFirst" or "WorkerCanceled" or "WorkerProtocolFault")
        {
            Assert.Equal(FunctionRpcRelaySide.Worker, terminal.Side);
        }

        if (retirementPath is "RuntimeCanceled" or "WorkerCanceled")
        {
            Assert.Equal(FunctionRpcRelayTerminationReason.Canceled, terminal.Reason);
        }

        if (string.Equals(retirementPath, "WorkerProtocolFault", StringComparison.Ordinal))
        {
            Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, terminal.Reason);
            Assert.IsType<System.IO.InvalidDataException>(terminal.Exception);
        }

        Assert.True(manager.State.RemovalRequested);
        Assert.False(manager.RequestRemoval());
        manager.SetAuthenticationConfigurationError(false);
        Assert.Equal(removed.IsUnhealthy, (await ReadManagementStateAsync(management, timeout.Token)).IsUnhealthy);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await AssertLateStreamRejectedAsync(factory, FunctionRpcRelaySide.Worker, timeout.Token);
    }

    [Theory]
    [InlineData("Preconfigured", null, 16, false, false)]
    [InlineData("Preconfigured", 10, 10, false, false)]
    [InlineData("SpecializationRequired", null, 16, false, false)]
    [InlineData("SpecializationRequired", 10, 10, false, false)]
    [InlineData("Preconfigured", null, 16, true, false)]
    [InlineData("SpecializationRequired", 10, 10, true, false)]
    [InlineData("SpecializationRequired", 10, 10, true, true)]
    public async Task RemovalRequested_IsStickyPollableAndClosesNewAdmission(
        string startupMode,
        int? maxHttpConcurrency,
        int expectedConcurrency,
        bool hasAuthenticationError,
        bool isDiscoveryWorker)
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
        Metadata runtimeHeaders = CreateRuntimeIdentityHeaders("valid");
        await using RelayClient runtime = CreateClient(
            factory.GetFunctionRpcAddress(FunctionRpcRelaySide.Runtime), timeout.Token, runtimeHeaders);
        WorkerAssignRequest assignment = new()
        {
            StartupMode = startupMode,
            FunctionAppName = "test-app",
            FunctionGroupName = isDiscoveryWorker ? "deployment:validation" : "http",
            HttpProxyEndpoint = "worker-pod.example:48801",
            IsAlwaysReady = false,
            Environment = [],
            FunctionAppDirectory = "/home/site/wwwroot"
        };
        if (maxHttpConcurrency is { } concurrency)
        {
            assignment.MaxHttpConcurrency = concurrency;
        }

        StreamingMessage initialized = await InitializeAndAssignWorkerAsync(
            worker, runtime, management, assignment,
            WorkerHttpForwardingTests.GetAddress(destination).AbsoluteUri, timeout.Token);
        Assert.Equal("https://worker-pod.example:48801/", initialized.WorkerInitResponse.Capabilities["HttpUri"]);
        Assert.Equal(assignment.FunctionGroupName, initialized.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        if (isDiscoveryWorker)
        {
            Assert.False(initialized.WorkerInitResponse.Capabilities.ContainsKey(MaxHttpConcurrencyCapability));
        }
        else
        {
            Assert.Equal(
                expectedConcurrency.ToString(CultureInfo.InvariantCulture),
                initialized.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability]);
        }
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        Assert.Equal(expectedConcurrency, manager.State.MaxHttpConcurrency);
        long sessionId = Assert.IsType<long>(manager.State.SessionId);
        DateTimeOffset startupTime = manager.State.StartupTime;
        while (!manager.State.IsRuntimeInitialized)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }

        manager.SetAuthenticationConfigurationError(hasAuthenticationError);
        WorkerOriginState running = await ReadManagementStateAsync(management, timeout.Token);
        Assert.Equal(WorkerStatus.Running, running.Status);
        Assert.Equal(WorkerLinkState.Linked, running.Link.State);
        Assert.Equal(hasAuthenticationError, running.IsUnhealthy);

        using (HttpResponseMessage before = await http.GetAsync("/before-removal", timeout.Token))
        {
            Assert.Equal(HttpStatusCode.NoContent, before.StatusCode);
        }

        Task<HttpResponseMessage> removalPoll = PollManagementStateAsync(management, running, timeout.Token);
        await WaitForManagementPollAsync(manager, timeout.Token);
        await runtime.CompleteRequestAsync(timeout.Token);
        await Task.WhenAll(
            runtime.WaitForTerminationAsync(timeout.Token),
            worker.WaitForTerminationAsync(timeout.Token));
        using HttpResponseMessage removalResponse = await removalPoll;
        WorkerOriginState removed = await ReadManagementStateAsync(removalResponse, timeout.Token);

        Assert.Equal(WorkerStatus.RemovalRequested, removed.Status);
        Assert.Equal(assignment.FunctionGroupName, removed.FunctionGroup);
        Assert.True(removed.IsUnhealthy);
        Assert.Equal(WorkerLinkState.Unlinked, removed.Link.State);
        Assert.Null(removed.Link.Runtime);
        Assert.Equal(WorkerStatus.RemovalRequested, (await ReadManagementStateAsync(management, timeout.Token)).Status);
        WorkerPodState removedState = manager.State;
        Assert.Null(removedState.FunctionAppName);
        Assert.Equal(assignment.FunctionGroupName, removedState.FunctionGroupName);
        Assert.Null(removedState.HttpProxyEndpoint);
        Assert.Null(removedState.MaxHttpConcurrency);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(sessionId).HttpProxyEndpoint);
        Assert.Equal(startupTime, removedState.StartupTime);
        Assert.False(manager.RequestRemoval());
        Assert.Same(removedState, manager.State);

        using (HttpResponseMessage reassignment = await PutManagementJsonAsync(
            management,
            "/admin/worker/assignment",
            ManagementAssignment,
            timeout.Token))
        {
            await AssertManagementErrorAsync(reassignment, HttpStatusCode.ServiceUnavailable, "WorkerNotReady", timeout.Token);
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

        Assert.Same(removedState, manager.State);
    }

    private static async Task<StreamingMessage> InitializeAndAssignWorkerAsync(
        RelayClient worker,
        RelayClient runtime,
        HttpClient management,
        WorkerAssignRequest assignment,
        string httpUri,
        CancellationToken cancellationToken)
    {
        StreamingMessage start = CreateStartStream();
        await worker.WriteAsync(start, cancellationToken);
        Assert.Equal(start, await runtime.ReadAsync(cancellationToken));
        using (HttpResponseMessage assigned = await management.PutAsJsonAsync(
            "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, cancellationToken))
        {
            Assert.Equal(HttpStatusCode.Created, assigned.StatusCode);
        }

        StreamingMessage request = new()
        {
            RequestId = "worker-init",
            WorkerInitRequest = new() { HostVersion = "test-host" }
        };
        await runtime.WriteAsync(request, cancellationToken);
        Assert.Equal(request, await worker.ReadAsync(cancellationToken));
        await worker.WriteAsync(CreateInitResponse(request.RequestId, httpUri), cancellationToken);
        StreamingMessage initialized = await runtime.ReadAsync(cancellationToken);
        Assert.NotNull(initialized.WorkerInitResponse);
        return initialized;
    }

    private static async Task<WorkerOriginState> WaitForRemovalStateAsync(
        HttpClient management,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            WorkerOriginState state = await ReadManagementStateAsync(management, cancellationToken);
            if (state.Status == WorkerStatus.RemovalRequested)
            {
                return state;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(10), cancellationToken);
        }
    }
}
