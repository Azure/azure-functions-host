// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Google.Protobuf;
using Grpc.Core;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests;

public partial class FunctionRpcRelayTests
{
    private const string FunctionGroupNameCapability = "FunctionGroupName";
    private const string MaxHttpConcurrencyCapability = "MaxHttpConcurrency";

    [Theory]
    [InlineData("http", null, "16")]
    [InlineData("HtTp", 10, "10")]
    [InlineData("http", 1, "1")]
    [InlineData("http", 1000, "1000")]
    [InlineData("background", null, null)]
    [InlineData("background", 0, null)]
    [InlineData("background", 10, null)]
    public async Task Relay_Initialization_UsesOnlyMatchingAssignmentHttpConcurrency(string? group, int? supplied, string? expected)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        if (group is not null)
        {
            WorkerAssignRequest assignment = new()
            {
                StartupMode = nameof(WorkerStartupMode.Preconfigured),
                FunctionAppName = "test-app",
                FunctionGroupName = group,
                IsAlwaysReady = false,
                Environment = [],
                FunctionAppDirectory = string.Empty,
            };
            if (supplied is { } concurrency)
            {
                assignment.MaxHttpConcurrency = concurrency;
            }

            using HttpClient client = factory.CreateWorkerProxyClient();
            using HttpResponseMessage assignmentResponse = await client.PutAsJsonAsync(
                "/admin/worker/assignment", assignment, WorkerProxyJsonContext.Default.WorkerAssignRequest, timeout.Token);
            Assert.Equal(HttpStatusCode.Created, assignmentResponse.StatusCode);
            Assert.Equal(group, manager.State.FunctionGroupName);
        }

        StreamingMessage response = CreateInitResponse("init", null);
        response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "worker-group";
        response.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability] = "999";
        await worker.WriteAsync(response, timeout.Token);
        StreamingMessage forwarded = await runtime.ReadAsync(timeout.Token);
        Assert.Equal(group, forwarded.WorkerInitResponse.Capabilities.TryGetValue(FunctionGroupNameCapability, out string? actualGroup) ? actualGroup : null);
        Assert.Equal(expected, forwarded.WorkerInitResponse.Capabilities.TryGetValue(MaxHttpConcurrencyCapability, out string? actual) ? actual : null);

        response.RequestId = "repeated-init";
        response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "http";
        response.WorkerInitResponse.Capabilities[MaxHttpConcurrencyCapability] = "123";
        await worker.WriteAsync(response, timeout.Token);
        StreamingMessage replay = await runtime.ReadAsync(timeout.Token);
        Assert.Equal(forwarded.WorkerInitResponse.Capabilities, replay.WorkerInitResponse.Capabilities);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(100)]
    public async Task Relay_PreservesOpaqueHostConfigurationThroughInitialAndRepeatedCapabilityFinalization(int status)
    {
        TestCapabilityFinalizer finalizer = new((capabilities, _) =>
        {
            capabilities["custom-capability"] = "finalized";
            return null;
        });
        await using WorkerProxyWebApplicationFactory factory = new(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkerCapabilityFinalizer>(finalizer)));
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        Assert.Equal(WorkerAssignmentResult.Created,
            factory.Services.GetRequiredService<WorkerPodStateManager>().Assign(CreateWorkerAssignment("assigned-group")));
        StreamingMessage request = new() { WorkerInitRequest = new() };
        await runtime.WriteAsync(request, timeout.Token);
        Assert.Equal(request, await worker.ReadAsync(timeout.Token));

        StreamingMessage response = CreateInitResponse("init", null);
        response.WorkerInitResponse.HostConfiguration = new()
        {
            Status = (HostConfiguration.Types.Status)status,
            Content = ByteString.CopyFrom(new byte[65_537]),
        };
        StreamingMessage expected = response.Clone();
        expected.WorkerInitResponse.Capabilities["custom-capability"] = "finalized";
        expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "assigned-group";
        expected.WorkerInitResponse.Capabilities["IsAlwaysReady"] = bool.FalseString;
        await worker.WriteAsync(response, timeout.Token);
        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));

        StreamingMessage repeated = CreateInitResponse("repeated", null);
        repeated.WorkerInitResponse.HostConfiguration = new()
        {
            Status = (HostConfiguration.Types.Status)status,
            Content = ByteString.CopyFrom([0xFF, 0x00, 0xFE]),
        };
        StreamingMessage repeatedExpected = repeated.Clone();
        repeatedExpected.WorkerInitResponse.Capabilities.Clear();
        repeatedExpected.WorkerInitResponse.Capabilities.Add(expected.WorkerInitResponse.Capabilities);
        await worker.WriteAsync(repeated, timeout.Token);
        Assert.Equal(repeatedExpected, await runtime.ReadAsync(timeout.Token));
        Assert.Equal(1, finalizer.CallCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Relay_UsesInjectedFinalizerOnceIncludingEmptyCapabilities(bool rewrite)
    {
        Uri? destination = rewrite ? new("http://worker:1234/") : null;
        TestCapabilityFinalizer finalizer = new((capabilities, _) =>
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

        Assert.Equal(WorkerAssignmentResult.Created,
            factory.Services.GetRequiredService<WorkerPodStateManager>().Assign(CreateWorkerAssignment("assigned-group")));
        StreamingMessage response = CreateInitResponse("successful-init", null);
        response.WorkerInitResponse.Capabilities.Clear();
        StreamingMessage expected = response.Clone();
        expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "assigned-group";
        expected.WorkerInitResponse.Capabilities["IsAlwaysReady"] = bool.FalseString;
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
        TestCapabilityFinalizer finalizer = new((capabilities, _) =>
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
        TestCapabilityFinalizer finalizer = new((capabilities, _) =>
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
            Assert.Equal(WorkerAssignmentState.Terminated, manager.State.AssignmentState);
        }
        finally
        {
            release.Set();
        }

        await returned.Task.WaitAsync(timeout.Token);
        await Task.WhenAll(runtime.WaitForTerminationAsync(timeout.Token), worker.WaitForTerminationAsync(timeout.Token));
        Assert.Equal(FunctionRpcRelayTerminationReason.Shutdown, relay.LastTerminalState?.Reason);
        Assert.Null(relay.WorkerHttpDestination);
        Assert.Equal(WorkerAssignmentState.Terminated, manager.State.AssignmentState);
        Assert.Equal(4, manager.State.Revision);
    }

    [Fact]
    public async Task Relay_AssignmentReplayDuringCapabilityFinalization_UsesOneConsistentSnapshot()
    {
        using ManualResetEventSlim release = new(initialState: false);
        TaskCompletionSource<string?> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TestCapabilityFinalizer finalizer = new((capabilities, httpProxyEndpoint) =>
        {
            entered.TrySetResult(httpProxyEndpoint);
            release.Wait();
            if (httpProxyEndpoint is not null && capabilities.ContainsKey("HttpUri"))
            {
                capabilities["HttpUri"] = httpProxyEndpoint;
            }

            return null;
        });
        await using WorkerProxyWebApplicationFactory factory = new(
            configureServices: services => services.Replace(ServiceDescriptor.Singleton<IWorkerCapabilityFinalizer>(finalizer)));
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        WorkerAssignment assignment = CreateWorkerAssignment("assigned-group", 10, "assigned.example:28080");
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));

        try
        {
            await worker.WriteAsync(CreateInitResponse("init", null), timeout.Token);
            Assert.Equal("https://assigned.example:28080/", await entered.Task.WaitAsync(timeout.Token));

            Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        }
        finally
        {
            release.Set();
        }

        StreamingMessage forwarded = await runtime.ReadAsync(timeout.Token);
        Assert.False(forwarded.WorkerInitResponse.Capabilities.ContainsKey("HttpUri"));
        Assert.Equal("assigned-group", forwarded.WorkerInitResponse.Capabilities[FunctionGroupNameCapability]);
        Assert.False(forwarded.WorkerInitResponse.Capabilities.ContainsKey(MaxHttpConcurrencyCapability));
        Assert.Equal("https://assigned.example:28080/",
            factory.Services.GetRequiredService<WorkerPodStateManager>().GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);
    }

    [Theory]
    [InlineData(null, "assigned-group", "assigned-group", false)]
    [InlineData("worker-group", "assigned-group", "assigned-group", true)]
    public async Task Relay_Initialization_AdvertisesOnlyAssignedFunctionGroup(string? workerSuppliedGroup,
        string? platformSuppliedGroup, string? expectedAdvertisedGroup, bool alwaysReady)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        if (platformSuppliedGroup is not null)
        {
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment(platformSuppliedGroup, isAlwaysReady: alwaysReady)));
        }

        StreamingMessage response = CreateInitResponse("init", null);
        response.WorkerInitResponse.Capabilities["IsAlwaysReady"] = (!alwaysReady).ToString();
        if (workerSuppliedGroup is not null)
        {
            response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = workerSuppliedGroup;
        }

        StreamingMessage expected = CreateInitResponse("init", null);
        if (expectedAdvertisedGroup is not null)
        {
            expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = expectedAdvertisedGroup;
            expected.WorkerInitResponse.Capabilities["IsAlwaysReady"] = alwaysReady.ToString();
        }

        await worker.WriteAsync(response, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
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
        repeated.WorkerInitResponse.Capabilities["IsAlwaysReady"] = bool.TrueString;
        StreamingMessage expected = repeated.Clone();
        expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = assignment.FunctionGroupName;
        expected.WorkerInitResponse.Capabilities["IsAlwaysReady"] = bool.FalseString;

        await worker.WriteAsync(repeated, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
    }

    [Fact]
    public async Task Relay_ReplacementSessionAfterFailedAssignment_CannotReuseAssignmentCapabilities()
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        WorkerAssignment failedAssignment = await FailAssignedSessionAsync(factory, timeout.Token);
        Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
        Assert.Equal(failedAssignment.FunctionGroupName, manager.State.FunctionGroupName);
        Assert.Equal(failedAssignment.MaxHttpConcurrency, manager.State.MaxHttpConcurrency);
        WorkerPodState failed = manager.State;
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        Grpc.Core.RpcException runtimeFailure = await runtime.WriteAndReadRejectionAsync(
            new StreamingMessage { WorkerInitRequest = new() }, timeout.Token);
        Grpc.Core.RpcException workerFailure = await worker.WriteAndReadRejectionAsync(
            CreateStartStream("replacement"), timeout.Token);

        Assert.Equal(StatusCode.Unavailable, runtimeFailure.StatusCode);
        Assert.Equal(StatusCode.Unavailable, workerFailure.StatusCode);
        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(failedAssignment));
        Assert.False(manager.State.IsWorkerReady);
        Assert.Same(failed, manager.State);
    }

    private static async Task<WorkerAssignment> FailAssignedSessionAsync(WorkerProxyWebApplicationFactory factory,
        CancellationToken cancellationToken)
    {
        FunctionRpcRelay relay = factory.Services.GetRequiredService<FunctionRpcRelay>();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, cancellationToken);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, cancellationToken);
        await ExchangeAsync(runtime, worker, "assigned", cancellationToken);
        WorkerAssignment assignment = CreateWorkerAssignment("http", maxHttpConcurrency: 10);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        await worker.CompleteRequestAsync(cancellationToken);
        await Task.WhenAll(runtime.WaitForTerminationAsync(cancellationToken), worker.WaitForTerminationAsync(cancellationToken));
        await WaitForReleaseAsync(relay, cancellationToken);
        return assignment;
    }

    private sealed class TestCapabilityFinalizer(
        Func<IDictionary<string, string>, string?, Uri?> finalize) : IWorkerCapabilityFinalizer
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Uri? FinalizeCapabilities(IDictionary<string, string> capabilities, string? httpProxyEndpoint)
        {
            Interlocked.Increment(ref _callCount);
            return finalize(capabilities, httpProxyEndpoint);
        }
    }
}
