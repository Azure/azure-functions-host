// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Xunit;

namespace Microsoft.Azure.Functions.WorkerProxy.Tests;

public class WorkerPodStateManagerTests
{
    [Fact]
    public void InitialStatus_IsNone()
    {
        var manager = new WorkerPodStateManager();

        Assert.Equal(WorkerPodStatus.None, manager.CurrentStatus);
    }

    [Fact]
    public void UpdatePodStatus_ChangesCurrentStatus()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        Assert.Equal(WorkerPodStatus.ReadyForRequest, manager.CurrentStatus);
    }

    [Fact]
    public void UpdatePodStatus_SameValue_DoesNotIncrementRevision()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state1 = manager.GetCurrentState();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state2 = manager.GetCurrentState();

        Assert.Equal(state1.RevisionId, state2.RevisionId);
    }

    [Fact]
    public void UpdatePodStatus_DifferentValue_IncrementsRevision()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        var state1 = manager.GetCurrentState();

        manager.UpdatePodStatus(WorkerPodStatus.Draining);
        var state2 = manager.GetCurrentState();

        Assert.Equal(state1.RevisionId + 1, state2.RevisionId);
    }

    [Fact]
    public void GetCurrentState_PopulatesFromPodStatus()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        manager.UpdatePodStatus(WorkerPodStatus.Draining);

        var state = manager.GetCurrentState();

        Assert.Equal(WorkerPodStatus.ReadyForRequest, state.CurrentPodStatusTransition.FromPodStatus);
        Assert.Equal(WorkerPodStatus.Draining, state.CurrentPodStatusTransition.ToPodStatus);
    }

    [Fact]
    public void GetCurrentState_PopulatesFromHealthStatus()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdateHealthStatus(WorkerPodHealthStatus.Healthy);
        manager.UpdateHealthStatus(WorkerPodHealthStatus.Unhealthy);

        var state = manager.GetCurrentState();

        Assert.Equal(WorkerPodHealthStatus.Healthy, state.CurrentPodHealthStatusTransition.FromPodStatus);
        Assert.Equal(WorkerPodHealthStatus.Unhealthy, state.CurrentPodHealthStatusTransition.ToPodStatus);
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnsImmediately_WhenRevisionDiffers()
    {
        var manager = new WorkerPodStateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        // Client has revision 0, current is 1 — should return immediately.
        var result = await manager.WaitForChangeAsync(0, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, result!.CurrentPodStatusTransition.ToPodStatus);
    }

    [Fact]
    public async Task WaitForChangeAsync_BlocksUntilStateChanges()
    {
        var manager = new WorkerPodStateManager();
        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var currentRevision = manager.GetCurrentState().RevisionId;

        // Start waiting — client is up to date.
        var waitTask = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);

        // Should not complete yet.
        await Task.Delay(50);
        Assert.False(waitTask.IsCompleted);

        // Trigger a state change.
        manager.UpdatePodStatus(WorkerPodStatus.Draining);

        var result = await waitTask;

        Assert.NotNull(result);
        Assert.Equal(WorkerPodStatus.Draining, result!.CurrentPodStatusTransition.ToPodStatus);
    }

    [Fact]
    public async Task WaitForChangeAsync_ReturnsNull_OnTimeout()
    {
        var manager = new WorkerPodStateManager();
        var currentRevision = manager.GetCurrentState().RevisionId;

        // Use a short cancellation to avoid waiting the full 60s.
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var result = await manager.WaitForChangeAsync(currentRevision, cts.Token);

        // Should return null (timeout / cancellation) without throwing.
        // The implementation catches TimeoutException and returns null.
        // Cancellation may also trigger — either way, no state change occurred.
        Assert.Null(result);
    }

    [Fact]
    public async Task WaitForChangeAsync_MultipleListeners_AllNotified()
    {
        var manager = new WorkerPodStateManager();
        var currentRevision = manager.GetCurrentState().RevisionId;

        var wait1 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);
        var wait2 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);
        var wait3 = manager.WaitForChangeAsync(currentRevision, CancellationToken.None);

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);

        var results = await Task.WhenAll(wait1, wait2, wait3);

        Assert.All(results, r =>
        {
            Assert.NotNull(r);
            Assert.Equal(WorkerPodStatus.ReadyForRequest, r!.CurrentPodStatusTransition.ToPodStatus);
        });
    }

    [Fact]
    public void FullLifecycle_TracksAllTransitions()
    {
        var manager = new WorkerPodStateManager();

        manager.UpdatePodStatus(WorkerPodStatus.ReadyForRequest);
        manager.UpdatePodStatus(WorkerPodStatus.Draining);
        manager.UpdatePodStatus(WorkerPodStatus.DrainCompleted);
        manager.UpdatePodStatus(WorkerPodStatus.MarkForDeletion);

        var state = manager.GetCurrentState();

        Assert.Equal(WorkerPodStatus.DrainCompleted, state.CurrentPodStatusTransition.FromPodStatus);
        Assert.Equal(WorkerPodStatus.MarkForDeletion, state.CurrentPodStatusTransition.ToPodStatus);
        Assert.Equal(4, state.RevisionId);
    }
}
