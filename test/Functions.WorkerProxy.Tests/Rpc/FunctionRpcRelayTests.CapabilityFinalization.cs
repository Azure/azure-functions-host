// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
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
    private const string MaxConcurrencyCapability = "MaxConcurrency";

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
    [InlineData(null, "assigned-group", "assigned-group")]
    [InlineData("worker-group", "assigned-group", "assigned-group")]
    [InlineData(null, null, null)]
    [InlineData("worker-group", null, null)]
    public async Task Relay_Initialization_AdvertisesOnlyAssignedFunctionGroup(string? workerSuppliedGroup,
        string? platformSuppliedGroup, string? expectedAdvertisedGroup)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        if (platformSuppliedGroup is not null)
        {
            Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment(platformSuppliedGroup)));
        }

        StreamingMessage response = CreateInitResponse("init", null);
        response.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "worker-supplied-invalid-value";
        if (workerSuppliedGroup is not null)
        {
            response.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = workerSuppliedGroup;
        }

        StreamingMessage expected = CreateInitResponse("init", null);
        if (expectedAdvertisedGroup is not null)
        {
            expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = expectedAdvertisedGroup;
            expected.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "16";
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
        WorkerAssignment assignment = CreateWorkerAssignment(maxConcurrency: 32);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        await worker.WriteAsync(CreateInitResponse("init", null), timeout.Token);
        await runtime.ReadAsync(timeout.Token);
        StreamingMessage repeated = CreateInitResponse("repeated-init", null);
        repeated.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = "changed-group";
        repeated.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "999";
        StreamingMessage expected = repeated.Clone();
        expected.WorkerInitResponse.Capabilities[FunctionGroupNameCapability] = assignment.FunctionGroupName;
        expected.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "32";

        await worker.WriteAsync(repeated, timeout.Token);

        Assert.Equal(expected, await runtime.ReadAsync(timeout.Token));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(int.MaxValue)]
    public async Task Relay_Initialization_AdvertisesAssignedMaxConcurrency(int maxConcurrency)
    {
        await using WorkerProxyWebApplicationFactory factory = CreateFactory();
        WorkerPodStateManager manager = factory.Services.GetRequiredService<WorkerPodStateManager>();
        using CancellationTokenSource timeout = new(TestTimeout);
        await using RelayClient runtime = CreateClient(factory, FunctionRpcRelaySide.Runtime, timeout.Token);
        await using RelayClient worker = CreateClient(factory, FunctionRpcRelaySide.Worker, timeout.Token);
        await ExchangeAsync(runtime, worker, "attach", timeout.Token);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateWorkerAssignment(maxConcurrency: maxConcurrency)));
        StreamingMessage response = CreateInitResponse("init", null);
        response.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "999";

        await worker.WriteAsync(response, timeout.Token);
        StreamingMessage forwarded = await runtime.ReadAsync(timeout.Token);

        Assert.Equal(maxConcurrency.ToString(CultureInfo.InvariantCulture),
            forwarded.WorkerInitResponse.Capabilities[MaxConcurrencyCapability]);
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
        response.WorkerInitResponse.Capabilities[MaxConcurrencyCapability] = "128";
        StreamingMessage expected = response.Clone();
        expected.WorkerInitResponse.Capabilities.Remove(MaxConcurrencyCapability);

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
