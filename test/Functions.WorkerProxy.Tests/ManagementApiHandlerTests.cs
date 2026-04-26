// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class ManagementApiHandlerTests
{
    private static readonly RelayOptions TestOptions = new(50051, 50052, 50053, null, "http://localhost:50053", "test-pod");

    private static WorkerPodStateManager CreateStateManager() => new(TestOptions);

    private static FunctionRpcRelay CreateRelay(WorkerPodStateManager stateManager) =>
        new(TestOptions, NullLogger<FunctionRpcRelay>.Instance, stateManager);

    // --- /admin/worker/ready ---

    [Fact]
    public void Ready_WhenNone_Returns503()
    {
        var stateManager = CreateStateManager();

        var result = ManagementApiHandlers.HandleReady(stateManager);

        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public void Ready_WhenReadyForRequest_Returns200()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var result = ManagementApiHandlers.HandleReady(stateManager);

        Assert.IsType<Ok>(result);
    }

    [Fact]
    public void Ready_WhenDraining_Returns503()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        stateManager.AcceptDrain(DrainReason.IdleScaleIn);

        var result = ManagementApiHandlers.HandleReady(stateManager);

        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    [Fact]
    public void Ready_WhenMarkedForDeletion_Returns503()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        stateManager.AcceptDrain(DrainReason.IdleScaleIn);
        stateManager.UpdatePodStatus(WorkerPodStatus.MarkedForDeletion);

        var result = ManagementApiHandlers.HandleReady(stateManager);

        var statusResult = Assert.IsType<StatusCodeHttpResult>(result);
        Assert.Equal(503, statusResult.StatusCode);
    }

    // --- /admin/worker/assign ---

    [Fact]
    public async Task Assign_NullBody_ReturnsBadRequest()
    {
        var stateManager = CreateStateManager();
        var relay = CreateRelay(stateManager);

        var result = await ManagementApiHandlers.HandleAssignAsync(null, stateManager, relay, CancellationToken.None);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Assign_StoresMetadata()
    {
        var stateManager = CreateStateManager();
        var relay = CreateRelay(stateManager);

        // Connect a fake worker so SpecializeWorkerAsync doesn't hang.
        relay._workerConnected.TrySetResult();

        // Simulate the worker init response flow.
        _ = Task.Run(async () =>
        {
            var success = new StatusResult { Status = StatusResult.Types.Status.Success };

            // Read and respond to WorkerInitRequest.
            await relay._toWorker.Reader.ReadAsync();
            relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage
            {
                WorkerInitResponse = new WorkerInitResponse { Result = success }
            });

            // Read and respond to FunctionEnvironmentReloadRequest.
            await relay._toWorker.Reader.ReadAsync();
            relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage
            {
                FunctionEnvironmentReloadResponse = new FunctionEnvironmentReloadResponse { Result = success }
            });

            // Read and respond to FunctionsMetadataRequest.
            await relay._toWorker.Reader.ReadAsync();
            relay._pendingWorkerResponse!.TrySetResult(new StreamingMessage
            {
                FunctionMetadataResponse = new FunctionMetadataResponse { Result = success }
            });
        });

        var request = new WorkerAssignRequest
        {
            FunctionAppName = "test-app",
            FunctionAppId = 1,
            FunctionGroupName = "http",
            IsAlwaysReady = true,
            Environment = new Dictionary<string, string> { ["FUNCTIONS_WORKER_RUNTIME"] = "node" }
        };

        var result = await ManagementApiHandlers.HandleAssignAsync(request, stateManager, relay, CancellationToken.None);

        Assert.IsType<Ok>(result);

        var state = stateManager.GetCurrentState();
        Assert.Equal("http", state.State.FunctionGroupName);
        Assert.True(state.State.IsAlwaysReady);
    }

    // --- /admin/worker/drain ---

    [Fact]
    public async Task Drain_NullBody_ReturnsBadRequest()
    {
        var stateManager = CreateStateManager();
        var relay = CreateRelay(stateManager);

        var result = await ManagementApiHandlers.HandleDrainAsync(null, stateManager, relay, NullLogger.Instance);

        Assert.IsType<BadRequest<string>>(result);
    }

    [Fact]
    public async Task Drain_MissingReason_ReturnsBadRequest()
    {
        var stateManager = CreateStateManager();
        var relay = CreateRelay(stateManager);
        var drainRequest = new WorkerDrainRequest { Reason = null };

        var result = await ManagementApiHandlers.HandleDrainAsync(drainRequest, stateManager, relay, NullLogger.Instance);

        Assert.IsType<BadRequest<string>>(result);
        Assert.Equal(WorkerPodStatus.None, stateManager.CurrentStatus);
    }

    [Fact]
    public async Task Drain_ValidReason_Returns202AndTransitionsToDraining()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var relay = CreateRelay(stateManager);
        var drainRequest = new WorkerDrainRequest { Reason = DrainReason.IdleScaleIn };

        var result = await ManagementApiHandlers.HandleDrainAsync(drainRequest, stateManager, relay, NullLogger.Instance);

        Assert.IsType<Accepted>(result);
        Assert.Equal(WorkerPodStatus.Draining, stateManager.CurrentStatus);
        Assert.True(stateManager.IsDraining);
    }

    [Fact]
    public async Task Drain_AlreadyDraining_Returns202WithoutResendingGrpc()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var relay = CreateRelay(stateManager);

        // First drain.
        await ManagementApiHandlers.HandleDrainAsync(
            new WorkerDrainRequest { Reason = DrainReason.IdleScaleIn },
            stateManager, relay, NullLogger.Instance);

        var revisionAfterFirst = stateManager.GetCurrentState().Revision;

        // Second drain — should be idempotent.
        var result = await ManagementApiHandlers.HandleDrainAsync(
            new WorkerDrainRequest { Reason = DrainReason.RuntimeStopping },
            stateManager, relay, NullLogger.Instance);

        Assert.IsType<Accepted>(result);
        Assert.Equal(revisionAfterFirst, stateManager.GetCurrentState().Revision);
    }

    [Fact]
    public async Task Drain_SetsReplacementPolicyBasedOnReason()
    {
        var stateManager = CreateStateManager();
        stateManager.SetAssignMetadata("http", false);
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var relay = CreateRelay(stateManager);
        var drainRequest = new WorkerDrainRequest { Reason = DrainReason.ReplaceWorkerKeepRuntime };

        await ManagementApiHandlers.HandleDrainAsync(drainRequest, stateManager, relay, NullLogger.Instance);

        var state = stateManager.GetCurrentState();
        Assert.Equal(ReplacementPolicy.SameRuntimeRefill, state.State.ReplacementPolicy);
    }

    // --- /admin/infra/instanceState ---

    [Fact]
    public async Task InstanceState_ReturnsCurrentState_WhenRevisionDiffers()
    {
        var stateManager = CreateStateManager();
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var result = await ManagementApiHandlers.HandleInstanceStateAsync(0, stateManager, CancellationToken.None);

        var okResult = Assert.IsType<Ok<WorkerInstanceState>>(result);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, okResult.Value!.State.PodStatus);
        Assert.Equal("FunctionsWorkerPod", okResult.Value.FunctionsContainerType);
        Assert.Equal("test-pod", okResult.Value.PodName);
    }

    [Fact]
    public async Task InstanceState_ReturnsNoContent_OnTimeout()
    {
        var stateManager = CreateStateManager();
        var currentRevision = stateManager.GetCurrentState().Revision;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await ManagementApiHandlers.HandleInstanceStateAsync(currentRevision, stateManager, cts.Token);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task InstanceState_RevisionZero_WithNoChanges_ReturnsNoContent()
    {
        var stateManager = CreateStateManager();

        // Initial _revisionId is 0, so clientRevision 0 means "up to date" — will long-poll.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var result = await ManagementApiHandlers.HandleInstanceStateAsync(0, stateManager, cts.Token);

        Assert.IsType<NoContent>(result);
    }

    [Fact]
    public async Task InstanceState_IncludesAssignMetadata()
    {
        var stateManager = CreateStateManager();
        stateManager.SetAssignMetadata("http", true);
        stateManager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var result = await ManagementApiHandlers.HandleInstanceStateAsync(0, stateManager, CancellationToken.None);

        var okResult = Assert.IsType<Ok<WorkerInstanceState>>(result);
        Assert.Equal("http", okResult.Value!.State.FunctionGroupName);
        Assert.True(okResult.Value.State.IsAlwaysReady);
    }
}
