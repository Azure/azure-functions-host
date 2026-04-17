// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerPodStateManagerTests
{
    private static WorkerPodStateManager CreateManager() =>
        new(new RelayOptions(50051, 50052, 50053, null, "http://localhost:50053", "test-pod"));

    [Fact]
    public void InitialStatus_IsNone()
    {
        var manager = CreateManager();

        Assert.Equal(WorkerPodStatus.None, manager.CurrentStatus);
    }

    [Fact]
    public void UpdatePodStatus_ChangesCurrentStatus()
    {
        var manager = CreateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        Assert.Equal(WorkerPodStatus.ReadyForRequest, manager.CurrentStatus);
    }

    [Fact]
    public void UpdatePodStatus_SameValue_DoesNotIncrementRevision()
    {
        var manager = CreateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state1 = manager.GetCurrentState();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state2 = manager.GetCurrentState();

        Assert.Equal(state1.Revision, state2.Revision);
    }

    [Fact]
    public void UpdatePodStatus_DifferentValue_IncrementsRevision()
    {
        var manager = CreateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state1 = manager.GetCurrentState();

        manager.UpdatePodStatus(WorkerPodStatus.Draining);
        var state2 = manager.GetCurrentState();

        Assert.Equal(state1.Revision + 1, state2.Revision);
    }

    [Fact]
    public void GetCurrentState_ReturnsCorrectPodStatus()
    {
        var manager = CreateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var state = manager.GetCurrentState();

        Assert.Equal(WorkerPodStatus.ReadyForRequest, state.State.PodStatus);
        Assert.Equal("FunctionsWorkerPod", state.FunctionsContainerType);
        Assert.Equal("test-pod", state.PodName);
    }

    [Fact]
    public void GetCurrentState_ReplacementPolicy_NullBeforeDrain()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var state = manager.GetCurrentState();

        Assert.Null(state.State.ReplacementPolicy);
    }

    [Fact]
    public void SetAssignMetadata_PopulatesStateFields()
    {
        var manager = CreateManager();

        manager.SetAssignMetadata("http", true);

        var state = manager.GetCurrentState();
        Assert.Equal("http", state.State.FunctionGroupName);
        Assert.True(state.State.IsAlwaysReady);
    }

    [Fact]
    public void SetRuntimePodName_PopulatesCorrelation()
    {
        var manager = CreateManager();

        manager.SetRuntimePodName("runtime-pod-042");

        var state = manager.GetCurrentState();
        Assert.Equal("runtime-pod-042", state.State.RuntimePodName);
    }

    // --- Drain reason tests ---

    [Fact]
    public void AcceptDrain_IdleScaleIn_MapsToNoReplacement()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var firstDrain = manager.AcceptDrain(DrainReason.IdleScaleIn);

        Assert.True(firstDrain);
        var state = manager.GetCurrentState();
        Assert.Equal(WorkerPodStatus.Draining, state.State.PodStatus);
        Assert.Equal(ReplacementPolicy.NoReplacement, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_RuntimeStopping_MapsToNoReplacement()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var firstDrain = manager.AcceptDrain(DrainReason.RuntimeStopping);

        Assert.True(firstDrain);
        var state = manager.GetCurrentState();
        Assert.Equal(WorkerPodStatus.Draining, state.State.PodStatus);
        Assert.Equal(ReplacementPolicy.NoReplacement, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_OrphanCleanup_MapsToNoReplacement()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var firstDrain = manager.AcceptDrain(DrainReason.OrphanCleanup);

        Assert.True(firstDrain);
        var state = manager.GetCurrentState();
        Assert.Equal(WorkerPodStatus.Draining, state.State.PodStatus);
        Assert.Equal(ReplacementPolicy.NoReplacement, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_ReplaceWorkerKeepRuntime_HttpGroup_MapsSameRuntimeRefill()
    {
        var manager = CreateManager();
        manager.SetAssignMetadata("http", false);
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        manager.AcceptDrain(DrainReason.ReplaceWorkerKeepRuntime);

        var state = manager.GetCurrentState();
        Assert.Equal(ReplacementPolicy.SameRuntimeRefill, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_ReplaceWorkerKeepRuntime_NonHttpGroup_MapsNoReplacement()
    {
        var manager = CreateManager();
        manager.SetAssignMetadata("", false);
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        manager.AcceptDrain(DrainReason.ReplaceWorkerKeepRuntime);

        var state = manager.GetCurrentState();
        Assert.Equal(ReplacementPolicy.NoReplacement, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_FirstReasonWins_SubsequentCallsIdempotent()
    {
        var manager = CreateManager();
        manager.SetAssignMetadata("http", false);
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var first = manager.AcceptDrain(DrainReason.ReplaceWorkerKeepRuntime);
        var revisionAfterFirst = manager.GetCurrentState().Revision;

        var second = manager.AcceptDrain(DrainReason.IdleScaleIn);
        var revisionAfterSecond = manager.GetCurrentState().Revision;

        Assert.True(first);
        Assert.False(second);
        Assert.Equal(revisionAfterFirst, revisionAfterSecond);

        // First reason's policy (SameRuntimeRefill) is sticky.
        var state = manager.GetCurrentState();
        Assert.Equal(ReplacementPolicy.SameRuntimeRefill, state.State.ReplacementPolicy);
    }

    [Fact]
    public void AcceptDrain_SetsStatusToDraining()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        manager.AcceptDrain(DrainReason.IdleScaleIn);

        Assert.Equal(WorkerPodStatus.Draining, manager.CurrentStatus);
    }

    [Fact]
    public void ReplacementPolicy_StickyThroughMarkedForDeletion()
    {
        var manager = CreateManager();
        manager.SetAssignMetadata("http", false);
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        manager.AcceptDrain(DrainReason.ReplaceWorkerKeepRuntime);
        manager.UpdatePodStatus(WorkerPodStatus.MarkedForDeletion);

        var state = manager.GetCurrentState();
        Assert.Equal(WorkerPodStatus.MarkedForDeletion, state.State.PodStatus);
        Assert.Equal(ReplacementPolicy.SameRuntimeRefill, state.State.ReplacementPolicy);
    }

    // --- Long-poll tests ---

    [Fact]
    public async Task WaitForChangeAsync_ReturnsImmediately_WhenRevisionDiffers()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        // Client has revision 0, current is 1 — should return immediately.
        var result = await manager.WaitForChangeAsync(0, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, result!.State.PodStatus);
    }

    [Fact]
    public async Task WaitForChangeAsync_BlocksUntilStateChanges()
    {
        var manager = CreateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var currentRevision = manager.GetCurrentState().Revision;

        // Start waiting — client is up to date.
        var waitTask = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);

        // Should not complete yet.
        await Task.Delay(50);
        Assert.False(waitTask.IsCompleted);

        // Trigger a state change.
        manager.UpdatePodStatus(WorkerPodStatus.Draining);

        var result = await waitTask;

        Assert.NotNull(result);
        Assert.Equal(WorkerPodStatus.Draining, result!.State.PodStatus);
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnsNull_OnTimeout()
    {
        var manager = CreateManager();
        var currentRevision = manager.GetCurrentState().Revision;

        // Use a short cancellation to avoid waiting the full 60s.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await manager.WaitForChangeAsync(currentRevision, cts.Token);

        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForChangeAsync_MultipleListeners_AllNotified()
    {
        var manager = CreateManager();
        var currentRevision = manager.GetCurrentState().Revision;

        var wait1 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);
        var wait2 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);
        var wait3 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var results = await Task.WhenAll(wait1, wait2, wait3);

        Assert.All(results, r =>
        {
            Assert.NotNull(r);
            Assert.Equal(WorkerPodStatus.ReadyForRequest, r!.State.PodStatus);
        });
    }

    [Fact]
    public void FullLifecycle_TracksAllTransitions()
    {
        var manager = CreateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        manager.AcceptDrain(DrainReason.IdleScaleIn);
        manager.UpdatePodStatus(WorkerPodStatus.MarkedForDeletion);

        var state = manager.GetCurrentState();

        Assert.Equal(WorkerPodStatus.MarkedForDeletion, state.State.PodStatus);
        Assert.Equal(ReplacementPolicy.NoReplacement, state.State.ReplacementPolicy);
        // ReadyForRequest (+1) + SetAssignMetadata would add if called, AcceptDrain (+1), MarkedForDeletion (+1) = 3
        Assert.Equal(3, state.Revision);
    }
}
