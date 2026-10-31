// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string FunctionGroupNameCapability = "FunctionGroupName";
    private const string IsAlwaysReadyCapability = "IsAlwaysReady";
    private const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_UsesInjectedFinalizerOnceIncludingEmptyCapabilities(bool rewrite)
    {
        Uri? destination = rewrite ? new("http://worker:1234/") : null;
        TestCapabilityFinalizer finalizer = new(capabilities =>
        {
            if (rewrite)
            {
                capabilities["custom-capability"] = "finalized";
            }

            return destination;
        });
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkerCapabilityFinalizer>(finalizer)));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        Assert.Equal(0, finalizer.CallCount);

        StreamingMessage failed = CreateInitResponse("failed-init", null);
        failed.WorkerInitResponse.Result.Status = StatusResult.Types.Status.Failure;
        await worker.WriteAsync(failed, timeout.Token);
        Assert.Equal(failed, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(0, finalizer.CallCount);

        StreamingMessage response = CreateInitResponse("successful-init", null);
        response.WorkerInitResponse.Capabilities.Clear();
        StreamingMessage expected = response.Clone();
        if (rewrite)
        {
            expected.WorkerInitResponse.Capabilities["custom-capability"] = "finalized";
        }

        await worker.WriteAsync(response, timeout.Token);
        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(1, finalizer.CallCount);
        Assert.Equal(destination, relay.WorkerHttpDestination);

        StreamingMessage repeated = CreateInitResponse("repeated-init", "http://ignored:5678/");
        StreamingMessage repeatedExpected = repeated.Clone();
        repeatedExpected.WorkerInitResponse.Capabilities.Clear();
        repeatedExpected.WorkerInitResponse.Capabilities.Add(expected.WorkerInitResponse.Capabilities);
        await worker.WriteAsync(repeated, timeout.Token);
        Assert.Equal(repeatedExpected, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(destination, relay.WorkerHttpDestination);
        Assert.Equal(1, finalizer.CallCount);
    }

    [Fact]
    public async Task Relay_FinalizerFailureTerminatesAssignedWorkerWithoutForwardingResponse()
    {
        InvalidOperationException failure = new("Injected finalization failure.");
        TestCapabilityFinalizer finalizer = new(capabilities =>
        {
            capabilities["partial"] = "must-not-be-forwarded";
            throw failure;
        });
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkerCapabilityFinalizer>(finalizer)));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        WorkerAssignment assignment = CreateWorkerAssignment();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(manager.State.Revision, timeout.Token);

        await worker.WriteAsync(CreateInitResponse("init", null), timeout.Token);

        Grpc.Core.RpcException exception = await Assert.ThrowsAsync<Grpc.Core.RpcException>(() => runtime.ReadAsync(timeout.Token));
        Assert.Equal(StatusCode.Unavailable, exception.StatusCode);
        Assert.Equal(StatusCode.Unavailable, await worker.WaitForTerminationAsync(timeout.Token));
        await WaitForReleaseAsync(relay, timeout.Token);
        WorkerPodState failed = Assert.IsType<WorkerPodState>((await poll).State);
        Assert.False(failed.IsWorkerReady);
        Assert.Equal(WorkerAssignmentState.Failed, failed.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(assignment));
        Assert.Null(relay.WorkerHttpDestination);
        Assert.Equal(FunctionRpcRelayTerminationReason.Faulted, relay.LastTerminalState?.Reason);
        Assert.Same(failure, relay.LastTerminalState?.Exception);
        Assert.Equal(1, finalizer.CallCount);
    }

    [Fact]
    public async Task Relay_ShutdownDuringFinalizationDoesNotRestoreDestinationOrReadiness()
    {
        using ManualResetEventSlim release = new(initialState: false);
        TaskCompletionSource<bool> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> returned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestCapabilityFinalizer finalizer = new(capabilities =>
        {
            entered.TrySetResult(true);
            release.Wait();
            capabilities["late-capability"] = "finalized";
            returned.TrySetResult(true);
            return new Uri("http://worker:1234/");
        });
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkerCapabilityFinalizer>(finalizer)));
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment()));

        try
        {
            await worker.WriteAsync(CreateInitResponse("init", null), timeout.Token);
            await entered.Task.WaitAsync(timeout.Token);
            await Task.Run(() => relay.StopAsync(timeout.Token), timeout.Token).WaitAsync(timeout.Token);
            Assert.Null(relay.WorkerHttpDestination);
            Assert.False(manager.State.IsWorkerReady);
            Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        }
        finally
        {
            release.Set();
        }

        await returned.Task.WaitAsync(timeout.Token);
        await Task.WhenAll(runtime.WaitForTerminationAsync(timeout.Token), worker.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        Assert.Null(relay.WorkerHttpDestination);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        Assert.Equal(4, manager.State.Revision);
    }

    [Theory]
    [InlineData("worker-group", "true", "999", true, true, "assigned-group", false, 7, "assigned-group", "False", "7")]
    [InlineData(null, null, null, true, true, "assigned-group", true, 13, "assigned-group", "True", "13")]
    [InlineData("worker-group", "true", "999", true, true, "assigned-group", false, 0, "assigned-group", "False", null)]
    [InlineData("worker-group", "true", "999", true, false, "assigned-group", false, 7, "assigned-group", null, null)]
    [InlineData("worker-group", "true", "999", false, false, null, false, 0, null, null, null)]
    public async Task Relay_Initialization_AdvertisesOnlyTrustedAssignmentCapabilities(
        string? workerSuppliedGroup,
        string? workerSuppliedAlwaysReady,
        string? workerSuppliedMaxHttpConcurrency,
        bool hasAssignment,
        bool authenticateAssignment,
        string? platformSuppliedGroup,
        bool platformSuppliedAlwaysReady,
        int platformSuppliedMaxHttpConcurrency,
        string? expectedAdvertisedGroup,
        string? expectedAdvertisedAlwaysReady,
        string? expectedAdvertisedMaxHttpConcurrency)
    {
        const string assignmentToken = "platform-assignment-token";
        Dictionary<string, string?> configuration = authenticateAssignment
            ? new()
            {
                [$"{WorkerProxyOptions.SectionName}:{nameof(WorkerProxyOptions.AssignmentAuthenticationToken)}"] = assignmentToken
            }
            : [];
        await using WorkerProxyWebApplicationFactory factory = new(configuration);
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        if (hasAssignment)
        {
            using HttpClient management = factory.CreateWorkerProxyClient();
            WorkerAssignRequest assignment = new()
            {
                StartupMode = WorkerStartupMode.Preconfigured.ToString(),
                FunctionAppName = "test-app",
                FunctionGroupName = platformSuppliedGroup,
                IsAlwaysReady = platformSuppliedAlwaysReady,
                MaxHttpConcurrency = platformSuppliedMaxHttpConcurrency,
                Environment = [],
                FunctionAppDirectory = string.Empty
            };
            if (authenticateAssignment)
            {
                using HttpResponseMessage missingAuthentication =
                    await SendAssignmentAsync(management, assignment, authenticationToken: null, timeout.Token);
                Assert.Equal(HttpStatusCode.Unauthorized, missingAuthentication.StatusCode);
                Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);

                using HttpResponseMessage invalidAuthentication =
                    await SendAssignmentAsync(management, assignment, authenticationToken: "invalid-token", timeout.Token);
                Assert.Equal(HttpStatusCode.Unauthorized, invalidAuthentication.StatusCode);
                Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
            }

            using HttpResponseMessage accepted = await SendAssignmentAsync(
                management,
                assignment,
                authenticateAssignment ? assignmentToken : null,
                timeout.Token);
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
            Assert.Equal(authenticateAssignment, manager.State.IsScaleInMetadataAuthenticated);
        }

        StreamingMessage response = CreateInitResponse("init", null);
        if (workerSuppliedGroup is not null)
        {
            response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = workerSuppliedGroup;
        }

        if (workerSuppliedAlwaysReady is not null)
        {
            response.WorkerInitResponse.Capabilities[IsAlwaysReadyCapability] = workerSuppliedAlwaysReady;
        }

        if (workerSuppliedMaxHttpConcurrency is not null)
        {
            response.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability] = workerSuppliedMaxHttpConcurrency;
        }

        StreamingMessage expected = CreateInitResponse("init", null);
        if (expectedAdvertisedGroup is not null)
        {
            expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = expectedAdvertisedGroup;
        }

        if (expectedAdvertisedAlwaysReady is not null)
        {
            expected.WorkerInitResponse.Capabilities[IsAlwaysReadyCapability] = expectedAdvertisedAlwaysReady;
        }

        if (expectedAdvertisedMaxHttpConcurrency is not null)
        {
            expected.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability] =
                expectedAdvertisedMaxHttpConcurrency;
        }

        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
    }

    private static async Task<HttpResponseMessage> SendAssignmentAsync(
        HttpClient client,
        WorkerAssignRequest assignment,
        string? authenticationToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Put, "/admin/worker/assignment")
        {
            Content = JsonContent.Create(assignment)
        };
        if (authenticationToken is not null)
        {
            request.Headers.Add(WorkerAssignmentAuthentication.HeaderName, authenticationToken);
        }

        return await client.SendAsync(request, cancellationToken);
    }

    [Fact]
    public async Task Relay_RepeatedInitialization_KeepsAssignedFunctionGroup()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        WorkerAssignment assignment = CreateWorkerAssignment();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        await worker.WriteAsync(CreateInitResponse("init", null), timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage repeated = CreateInitResponse("repeated-init", null);
        repeated.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "changed-group";
        StreamingMessage expected = repeated.Clone();
        expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = assignment.FunctionGroupName;

        await worker.WriteAsync(repeated, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task Relay_ReplacementSessionAfterFailedAssignment_DoesNotReuseFunctionGroup()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerAssignment failedAssignment = await FailAssignedSessionAsync(factory, timeout.Token);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        Assert.Equal(failedAssignment.FunctionGroupName, manager.State.FunctionGroupName);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "replacement", timeout.Token);
        StreamingMessage response = CreateInitResponse("init", null);
        StreamingMessage expected = response.Clone();

        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
    }

    private static async Task<WorkerAssignment> FailAssignedSessionAsync(WorkerProxyWebApplicationFactory factory,
        CancellationToken cancellationToken)
    {
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, cancellationToken);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, cancellationToken);
        await ExchangeAsync(runtime, worker, "assigned", cancellationToken);
        WorkerAssignment assignment = CreateWorkerAssignment();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        await worker.CompleteRequestAsync(cancellationToken);
        await Task.WhenAll(runtime.WaitForTerminationAsync(cancellationToken), worker.WaitForTerminationAsync(cancellationToken));
        await WaitForReleaseAsync(relay, cancellationToken);
        return assignment;
    }

    private sealed class TestCapabilityFinalizer(Func<IDictionary<string, string>, Uri?> finalize) : IWorkerCapabilityFinalizer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Uri? FinalizeCapabilities(IDictionary<string, string> capabilities)
        {
            Interlocked.Increment(ref _callCount);
            return finalize(capabilities);
        }
    }
}
