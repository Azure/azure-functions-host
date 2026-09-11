// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(assignment));
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
        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateWorkerAssignment()));

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
