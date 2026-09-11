// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.State;

public partial class WorkerPodStateManagerTests
{
    [Fact]
    public void InitialState_IsUnassignedAtRevisionZero()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        WorkerPodState state = manager.State;

        Assert.Equal("pod", state.PodName);
        Assert.Equal(0, state.Revision);
        Assert.Null(state.SessionId);
        Assert.False(state.IsWorkerAttached);
        Assert.False(state.IsWorkerReady);
        Assert.Null(state.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.Equal(WorkerPodStatus.None, state.PodStatus);
        Assert.Null(state.FunctionAppName);
        Assert.Null(state.FunctionGroupName);
        Assert.Null(state.IsAlwaysReady);
        Assert.Same(state, manager.State);
    }

    [Fact]
    public void Lifecycle_PublishesImmutableMonotonicSnapshots()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        WorkerPodState initial = manager.State;
        Assert.True(manager.OnWorkerAttached(1));
        WorkerPodState attached = manager.State;
        Assert.False(attached.IsWorkerReady);
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        WorkerPodState started = manager.State;
        Assert.True(started.IsWorkerReady);
        Assert.Equal(WorkerPodStatus.None, started.PodStatus);

        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateAssignment()));
        WorkerPodState assigned = manager.State;
        Assert.Equal(WorkerAssignmentState.Ready, assigned.AssignmentState);
        Assert.Equal(WorkerPodStatus.ReadyForRequest, assigned.PodStatus);
        Assert.Equal("app", assigned.FunctionAppName);
        Assert.Equal("http", assigned.FunctionGroupName);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("worker", assigned.WorkerId);
        Assert.Equal(1, assigned.SessionId);

        Assert.True(manager.OnSessionTerminated(1));
        WorkerPodState failed = manager.State;
        Assert.False(failed.IsWorkerReady);
        Assert.False(failed.IsWorkerAttached);
        Assert.Equal(WorkerAssignmentState.Failed, failed.AssignmentState);
        Assert.Equal(WorkerPodStatus.None, failed.PodStatus);
        Assert.Equal("app", failed.FunctionAppName);
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 },
            new[] { initial.Revision, attached.Revision, started.Revision, assigned.Revision, failed.Revision });

        Assert.False(initial.IsWorkerAttached);
        Assert.Null(attached.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, started.AssignmentState);
        Assert.True(assigned.IsWorkerReady);
    }

    [Fact]
    public void NotReadyAssignment_DoesNotClaimIdentityOrChangeRevision()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        WorkerPodState initial = manager.State;
        Assert.Equal(WorkerAssignmentResult.WorkerNotReady, manager.Assign(CreateAssignment("rejected")));
        Assert.Same(initial, manager.State);

        manager.OnWorkerAttached(1);
        WorkerPodState attached = manager.State;
        Assert.Equal(WorkerAssignmentResult.WorkerNotReady, manager.Assign(CreateAssignment("also-rejected")));
        Assert.Same(attached, manager.State);

        manager.OnWorkerStartStream(1, "worker");
        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateAssignment("accepted")));
        Assert.Equal("accepted", manager.State.FunctionAppName);
    }

    [Fact]
    public void AssignmentReplayAndConflict_DoNotChangeRevision()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateAssignment()));
        WorkerPodState state = manager.State;

        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateAssignment()));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
        Assert.Same(state, manager.State);
    }

    [Fact]
    public void TerminalFailure_IsStickyAndConflictingIdentityStillConflicts()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        manager.Assign(CreateAssignment());
        manager.OnSessionTerminated(1);
        WorkerPodState failed = manager.State;

        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(CreateAssignment()));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
        Assert.False(manager.OnWorkerAttached(2));
        Assert.False(manager.OnWorkerStartStream(2, "replacement"));
        Assert.False(manager.OnWorkerStartStream(1, "worker"));
        Assert.False(manager.OnSessionTerminated(1));
        Assert.Same(failed, manager.State);
    }

    [Fact]
    public void UnassignedSession_CanBeReplacedWithoutResettingRevision()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        manager.OnSessionTerminated(1);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.WorkerNotReady, manager.Assign(CreateAssignment()));

        Assert.True(manager.OnWorkerAttached(2));
        Assert.False(manager.State.IsWorkerReady);
        Assert.True(manager.OnWorkerStartStream(2, "replacement"));
        WorkerPodState replacement = manager.State;

        Assert.False(manager.OnWorkerAttached(1));
        Assert.False(manager.OnWorkerStartStream(1, "old-worker"));
        Assert.False(manager.OnSessionTerminated(1));
        Assert.Same(replacement, manager.State);
        Assert.Equal(5, replacement.Revision);
        Assert.Equal(WorkerAssignmentResult.Success, manager.Assign(CreateAssignment()));
        Assert.Equal(6, manager.State.Revision);
    }

    [Fact]
    public void RuntimeOnlyTermination_PreventsLateWorkerAttachmentForThatSession()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        WorkerPodState initial = manager.State;

        Assert.False(manager.OnSessionTerminated(1));
        Assert.False(manager.OnWorkerAttached(1));
        Assert.False(manager.OnWorkerStartStream(1, "late-worker"));
        Assert.Same(initial, manager.State);
        Assert.True(manager.OnWorkerAttached(2));
    }

    [Fact]
    public void DuplicateLifecycleNotifications_DoNotChangeRevision()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState ready = manager.State;

        Assert.False(manager.OnWorkerAttached(1));
        Assert.False(manager.OnWorkerStartStream(1, "worker"));
        Assert.Same(ready, manager.State);
    }

    [Fact]
    public void InconsistentLifecycleNotifications_AreRejectedWithoutMutation()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState ready = manager.State;

        Assert.Throws<InvalidOperationException>(() => manager.OnWorkerAttached(2));
        Assert.Throws<InvalidOperationException>(() => manager.OnSessionTerminated(2));
        Assert.Throws<InvalidOperationException>(() => manager.OnWorkerStartStream(1, "different-worker"));
        Assert.Same(ready, manager.State);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void InvalidWorkerIdentity_DoesNotEnableReadiness(string? workerId)
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        manager.OnWorkerAttached(1);
        WorkerPodState attached = manager.State;

        Assert.ThrowsAny<ArgumentException>(() => manager.OnWorkerStartStream(1, workerId!));
        Assert.Same(attached, manager.State);
        Assert.False(manager.State.IsWorkerReady);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidSessionIdentity_IsRejected(long sessionId)
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.OnWorkerAttached(sessionId));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.OnWorkerStartStream(sessionId, "worker"));
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.OnSessionTerminated(sessionId));
        Assert.Equal(0, manager.State.Revision);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(null)]
    public void MissingPodIdentity_IsRejected(string? podName)
    {
        Assert.ThrowsAny<ArgumentException>(() => new WorkerPodStateManager(CreateOptions(podName!), TimeProvider.System));
    }

    [Fact]
    public void NullTimeProvider_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerPodStateManager(CreateOptions(), null!));
    }

    [Fact]
    public void NullOptions_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new WorkerPodStateManager(null!, TimeProvider.System));
    }

    [Fact]
    public void PodIdentity_IsCapturedDuringConstruction()
    {
        WorkerProxyOptions options = new() { PodName = "original-pod" };
        WorkerPodStateManager manager = new(Options.Create(options), TimeProvider.System);

        options.PodName = "different-pod";
        manager.OnWorkerAttached(1);

        Assert.Equal("original-pod", manager.State.PodName);
    }

    [Fact]
    public void NullAssignment_IsRejectedWithoutMutation()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        WorkerPodState ready = manager.State;
        Assert.Throws<ArgumentNullException>(() => manager.Assign(null!));
        Assert.Same(ready, manager.State);
    }

    [Fact]
    public async Task ConcurrentEquivalentAssignments_PublishOnlyOnce()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<WorkerAssignmentResult>[] attempts = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
        {
            await start.Task;
            return manager.Assign(CreateAssignment());
        })).ToArray();

        start.SetResult();
        WorkerAssignmentResult[] results = await Task.WhenAll(attempts);

        Assert.All(results, result => Assert.Equal(WorkerAssignmentResult.Success, result));
        Assert.Equal(3, manager.State.Revision);
    }

    [Fact]
    public async Task ConcurrentConflictingAssignments_HaveOneWinningIdentity()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(string AppName, WorkerAssignmentResult Result)>[] attempts = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            string appName = $"app-{index}";
            return (appName, manager.Assign(CreateAssignment(appName)));
        })).ToArray();

        start.SetResult();
        (string AppName, WorkerAssignmentResult Result)[] results = await Task.WhenAll(attempts);
        (string AppName, WorkerAssignmentResult Result) winner = Assert.Single(results, result => result.Result == WorkerAssignmentResult.Success);

        Assert.Equal(winner.AppName, manager.State.FunctionAppName);
        Assert.Equal(31, results.Count(result => result.Result == WorkerAssignmentResult.AssignmentConflict));
        Assert.Equal(3, manager.State.Revision);
    }

    [Fact]
    public async Task AssignmentRacingTermination_CannotLeaveReadyState()
    {
        for (int iteration = 0; iteration < 32; iteration++)
        {
            WorkerPodStateManager manager = CreateReadyManager();
            TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<WorkerAssignmentResult> assignment = Task.Run(async () =>
            {
                await start.Task;
                return manager.Assign(CreateAssignment());
            });
            Task termination = Task.Run(async () =>
            {
                await start.Task;
                manager.OnSessionTerminated(1);
            });

            start.SetResult();
            await Task.WhenAll(assignment, termination);

            WorkerAssignmentResult result = await assignment;
            Assert.False(manager.State.IsWorkerReady);
            Assert.Equal(WorkerPodStatus.None, manager.State.PodStatus);
            if (result == WorkerAssignmentResult.Success)
            {
                Assert.Equal(WorkerAssignmentState.Failed, manager.State.AssignmentState);
                Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(CreateAssignment()));
                Assert.Equal(4, manager.State.Revision);
            }
            else
            {
                Assert.Equal(WorkerAssignmentResult.WorkerNotReady, result);
                Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
                Assert.Equal(3, manager.State.Revision);
            }
        }
    }

    private static WorkerPodStateManager CreateReadyManager()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        return manager;
    }

    private static WorkerAssignment CreateAssignment(string appName = "app")
        => new(appName, "http", false, new Dictionary<string, string> { ["SETTING"] = "value" }, "/home/site/wwwroot");

    private static IOptions<WorkerProxyOptions> CreateOptions(string podName = "pod")
        => Options.Create(new WorkerProxyOptions { PodName = podName });
}
