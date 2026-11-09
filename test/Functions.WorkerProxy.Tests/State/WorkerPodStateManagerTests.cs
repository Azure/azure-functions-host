// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.State;
using Microsoft.Extensions.Options;
using Xunit;

namespace Azure.Functions.WorkerProxy.Tests.State;

public partial class WorkerPodStateManagerTests
{
    [Fact]
    public void InitialState_IsUnassignedAtRevisionZero()
    {
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
        WorkerPodState state = manager.State;

        Assert.Equal("pod", state.PodName);
        Assert.Equal(0, state.Revision);
        Assert.Null(state.SessionId);
        Assert.False(state.IsWorkerAttached);
        Assert.False(state.IsWorkerReady);
        Assert.Null(state.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, state.AssignmentState);
        Assert.Null(state.StartupMode);
        Assert.Equal(WorkerPodStatus.Starting, state.PodStatus);
        Assert.False(state.IsDraining);
        Assert.Null(state.FunctionAppName);
        Assert.Null(state.FunctionGroupName);
        Assert.Null(state.IsAlwaysReady);
        Assert.False(state.HasAuthenticationConfigurationError);
        Assert.False(state.IsUnhealthy);
        Assert.Same(state, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthenticationConfigurationHealth_PublishesChangesWithoutFailingOrReplacingAssignment(bool assignmentFailed)
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        WorkerPodState initial = manager.State;
        manager.SetAuthenticationConfigurationError(true);
        WorkerPodState unavailable = manager.State;
        Assert.True(unavailable.IsUnhealthy);
        Assert.False(initial.IsUnhealthy);
        Assert.Equal(WorkerAssignmentState.Unassigned, unavailable.AssignmentState);
        Assert.Equal(1, unavailable.Revision);
        WorkerAssignment assignment = CreateAssignment(configVersion: 111, contentVersion: 222);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        WorkerPodState assigned = manager.State;
        Assert.True(assigned.IsUnhealthy);
        Assert.Equal(WorkerAssignmentState.Assigned, assigned.AssignmentState);
        Assert.False(assigned.IsWorkerAttached);
        manager.SetAuthenticationConfigurationError(true);
        Assert.Same(assigned, manager.State);
        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(assigned.Revision);

        manager.SetAuthenticationConfigurationError(false);
        WorkerPodState recovered = Assert.IsType<WorkerPodState>((await poll.WaitAsync(TestTimeout)).State);

        Assert.False(recovered.IsUnhealthy);
        Assert.Equal(assigned.Revision + 1, recovered.Revision);
        Assert.Equal(assigned.FunctionAppName, recovered.FunctionAppName);
        Assert.Equal(assigned.FunctionGroupName, recovered.FunctionGroupName);
        Assert.Equal(assigned.AssignmentState, recovered.AssignmentState);
        Assert.Equal(111, WorkerOriginState.FromState(recovered).ConfigLastModifiedTime);
        Assert.Equal(222, WorkerOriginState.FromState(recovered).ContentLastModifiedTime);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
        manager.OnWorkerAttached(1);
        manager.OnSessionTerminated(1, assignmentFailed);
        manager.SetAuthenticationConfigurationError(true);
        Assert.True(manager.State.IsUnhealthy);
        manager.SetAuthenticationConfigurationError(false);
        Assert.False(manager.State.HasAuthenticationConfigurationError);
        Assert.Equal(assignmentFailed, manager.State.IsUnhealthy);
        Assert.Equal(111, WorkerOriginState.FromState(manager.State).ConfigLastModifiedTime);
        Assert.Equal(222, WorkerOriginState.FromState(manager.State).ContentLastModifiedTime);
        Assert.Equal(assignmentFailed ? WorkerAssignmentState.Failed : WorkerAssignmentState.Terminated, manager.State.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(assignment));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Lifecycle_PublishesImmutableMonotonicSnapshots(bool specializationRequired)
    {
        WorkerStartupMode startupMode = specializationRequired ? WorkerStartupMode.SpecializationRequired : WorkerStartupMode.Preconfigured;
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
        WorkerPodState initial = manager.State;
        Assert.True(manager.OnWorkerAttached(1));
        WorkerPodState attached = manager.State;
        Assert.False(attached.IsWorkerReady);
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        WorkerPodState started = manager.State;
        Assert.True(started.IsWorkerReady);
        Assert.Equal(WorkerPodStatus.Starting, started.PodStatus);

        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment(startupMode: startupMode)));
        WorkerPodState assigned = manager.State;
        Assert.Equal(WorkerAssignmentState.Ready, assigned.AssignmentState);
        Assert.Equal(startupMode, assigned.StartupMode);
        Assert.Equal(WorkerPodStatus.Running, assigned.PodStatus);
        Assert.Equal("app", assigned.FunctionAppName);
        Assert.Equal("http", assigned.FunctionGroupName);
        Assert.False(assigned.IsAlwaysReady);
        Assert.Equal("worker", assigned.WorkerId);
        Assert.Equal(1, assigned.SessionId);

        Assert.True(manager.OnSessionTerminated(1));
        WorkerPodState failed = manager.State;
        Assert.False(failed.IsWorkerReady);
        Assert.False(failed.IsWorkerAttached);
        Assert.Equal(WorkerAssignmentState.Terminated, failed.AssignmentState);
        Assert.Equal(startupMode, failed.StartupMode);
        Assert.Equal(WorkerPodStatus.RemovalRequested, failed.PodStatus);
        Assert.Equal("app", failed.FunctionAppName);
        Assert.Equal(new long[] { 0, 1, 2, 3, 4 },
            new[] { initial.Revision, attached.Revision, started.Revision, assigned.Revision, failed.Revision });
        Assert.False(initial.IsWorkerAttached);
        Assert.Null(attached.WorkerId);
        Assert.Equal(WorkerAssignmentState.Unassigned, started.AssignmentState);
        Assert.Null(started.StartupMode);
        Assert.True(assigned.IsWorkerReady);
    }

    [Fact]
    public async Task WorkerDraining_PublishesOnceForCurrentReadySession()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment()));
        WorkerPodState ready = manager.State;
        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(ready.Revision);

        Assert.True(manager.OnWorkerDraining(1));
        WorkerPodState draining = Assert.IsType<WorkerPodState>((await poll.WaitAsync(TestTimeout)).State);

        Assert.Equal(ready.Revision + 1, draining.Revision);
        Assert.True(draining.IsDraining);
        Assert.Equal(WorkerPodStatus.Draining, draining.PodStatus);
        Assert.Equal(WorkerStatus.Draining, WorkerOriginState.FromState(draining).Status);
        Assert.False(manager.OnWorkerDraining(1));
        Assert.False(manager.OnWorkerDraining(2));
        Assert.Same(draining, manager.State);
    }

    [Fact]
    public void AssignmentBeforeWorkerAttachment_ClaimsIdentityAndBecomesReadyAfterStartStream()
    {
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
        WorkerAssignment assignment = new(WorkerStartupMode.SpecializationRequired, "accepted", "http", false,
            new Dictionary<string, string> { ["SETTING"] = "private-value" }, "/app", new Uri("https://127.0.0.1:48801/"));
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        WorkerPodState assigned = manager.State;
        Assert.Equal(1, assigned.Revision);
        Assert.Equal(WorkerAssignmentState.Assigned, assigned.AssignmentState);
        Assert.Equal("accepted", assigned.FunctionAppName);
        Assert.Equal(WorkerStartupMode.SpecializationRequired, assigned.StartupMode);
        Assert.False(assigned.IsWorkerReady);
        Assert.Equal(WorkerPodStatus.Starting, assigned.PodStatus);
        Assert.Equal(WorkerStatus.Starting, WorkerOriginState.FromState(assigned).Status);

        Assert.True(manager.OnWorkerAttached(1));
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        Assert.False(manager.State.IsWorkerReady);
        Assert.True(manager.OnWorkerStartStream(1, "worker"));
        Assert.Equal(3, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.True(manager.State.IsWorkerReady);
        Assert.Equal(WorkerPodStatus.Running, manager.State.PodStatus);
        Assert.Equal(WorkerStatus.Starting, WorkerOriginState.FromState(manager.State).Status);
        Assert.Equal(WorkerLinkState.Unlinked, WorkerOriginState.FromState(manager.State).Link.State);
        Assert.Equal("accepted", manager.State.FunctionAppName);
        Assert.Equal(WorkerStartupMode.SpecializationRequired, manager.State.StartupMode);
        Assert.Equal(assignment.HttpProxyEndpoint, manager.State.HttpProxyEndpoint);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
    }

    [Fact]
    public void AssignmentReplayAndConflict_DoNotChangeRevision()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment()));
        WorkerPodState state = manager.State;

        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(CreateAssignment()));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
        Assert.Same(state, manager.State);
    }

    [Fact]
    public void AcceptedAssignment_ReplacesEffectiveHttpProxyEndpointBeforePublishingState()
    {
        WorkerProxyOptions options = new() { PodName = "pod" };
        IOptions<WorkerProxyOptions> configuredOptions = Options.Create(options);
        WorkerPodStateManager manager = new(configuredOptions, TimeProvider.System);

        WorkerAssignment assignment = CreateAssignment(httpProxyEndpoint: "mapped.example:48801");
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(assignment));
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);

        manager.OnWorkerAttached(1);
        Assert.Null(manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);
        manager.OnWorkerStartStream(1, "worker");
        Assert.Equal(WorkerAssignmentState.Ready, manager.State.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        WorkerPodState assigned = manager.State;

        Assert.Equal("https://mapped.example:48801/", manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(assignment));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment(httpProxyEndpoint: "mapped.example:48802")));
        Assert.Same(assigned, manager.State);
        Assert.Equal("https://mapped.example:48801/", manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);

        manager.OnSessionTerminated(1);
        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(assignment));
        Assert.Null(manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);
        Assert.Equal(new Uri("https://mapped.example:48801/"), assignment.HttpProxyEndpoint);
    }

    [Fact]
    public void AcceptedAssignmentWithoutHttpProxyEndpoint_LeavesEffectiveEndpointUnset()
    {
        WorkerProxyOptions options = new() { PodName = "pod" };
        IOptions<WorkerProxyOptions> configuredOptions = Options.Create(options);
        WorkerPodStateManager manager = new(configuredOptions, TimeProvider.System);
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");

        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment()));

        Assert.Null(manager.GetWorkerCapabilitySnapshot(1).HttpProxyEndpoint);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WaitForAssignment_CompletesAfterAcceptedAssignment(bool workerReady)
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        if (workerReady)
        {
            manager.OnWorkerAttached(1);
            manager.OnWorkerStartStream(1, "worker");
        }

        WorkerAssignment accepted = CreateAssignment("accepted");
        Task<WorkerAssignment> wait = manager.WaitForAssignmentAsync();
        Assert.False(wait.IsCompleted);

        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(accepted));
        Assert.Same(accepted, await wait.WaitAsync(TestTimeout));
        Assert.Same(accepted, await manager.WaitForAssignmentAsync().WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitForAssignment_RejectedAssignmentDoesNotCompleteWaiter()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        Assert.True(manager.OnRuntimeAttached(1, new("namespace", "pod", new("accepted", 1, "stamp"))));
        Task<WorkerAssignment> wait = manager.WaitForAssignmentAsync();

        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("rejected")));
        Assert.False(wait.IsCompleted);

        WorkerAssignment accepted = CreateAssignment("accepted");
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(accepted));
        Assert.Same(accepted, await wait.WaitAsync(TestTimeout));
    }

    [Fact]
    public async Task WaitForAssignment_CancellationDoesNotCancelOtherWaiters()
    {
        WorkerPodStateManager manager = new(CreateOptions(), TimeProvider.System);
        using CancellationTokenSource cancellation = new();
        Task<WorkerAssignment> canceledWait = manager.WaitForAssignmentAsync(cancellation.Token);
        Task<WorkerAssignment> activeWait = manager.WaitForAssignmentAsync();

        cancellation.Cancel();
        OperationCanceledException exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => canceledWait.WaitAsync(TestTimeout));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(activeWait.IsCompleted);

        WorkerAssignment accepted = CreateAssignment();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(accepted));
        Assert.Same(accepted, await activeWait.WaitAsync(TestTimeout));
        Assert.Same(accepted, await manager.WaitForAssignmentAsync().WaitAsync(TestTimeout));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TerminalFailure_IsStickyAndConflictingIdentityStillConflicts(bool assignmentFailed)
    {
        WorkerPodStateManager manager = CreateReadyManager();
        manager.Assign(CreateAssignment());
        manager.OnSessionTerminated(1, assignmentFailed);
        WorkerPodState failed = manager.State;

        Assert.Equal(assignmentFailed ? WorkerAssignmentState.Failed : WorkerAssignmentState.Terminated, failed.AssignmentState);
        Assert.Equal(assignmentFailed, WorkerOriginState.FromState(failed).IsUnhealthy);
        Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(CreateAssignment()));
        Assert.Equal(WorkerAssignmentResult.AssignmentConflict, manager.Assign(CreateAssignment("other")));
        Assert.False(manager.OnWorkerAttached(2));
        Assert.False(manager.OnWorkerStartStream(2, "replacement"));
        Assert.False(manager.OnWorkerStartStream(1, "worker"));
        Assert.False(manager.OnSessionTerminated(1));
        Assert.Same(failed, manager.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RequestRemoval_PreservesKnownWorkerFailureWithoutCreatingOne(bool assignmentFailed)
    {
        WorkerPodStateManager manager = CreateReadyManager();
        manager.Assign(CreateAssignment());
        manager.OnSessionTerminated(1, assignmentFailed);

        Assert.True(manager.RequestRemoval());

        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.Equal(assignmentFailed, manager.State.IsUnhealthy);
        Assert.Equal(assignmentFailed, WorkerOriginState.FromState(manager.State).IsUnhealthy);
    }

    [Fact]
    public void UnassignedSession_CanBeReplacedWithoutResettingRevision()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        manager.OnSessionTerminated(1);
        Assert.Equal(WorkerAssignmentState.Unassigned, manager.State.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment()));
        Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);

        Assert.True(manager.OnWorkerAttached(2));
        Assert.False(manager.State.IsWorkerReady);
        Assert.True(manager.OnWorkerStartStream(2, "replacement"));
        WorkerPodState replacement = manager.State;

        Assert.False(manager.OnWorkerAttached(1));
        Assert.False(manager.OnWorkerStartStream(1, "old-worker"));
        Assert.False(manager.OnSessionTerminated(1));
        Assert.Same(replacement, manager.State);
        Assert.Equal(6, manager.State.Revision);
        Assert.Equal(WorkerAssignmentState.Ready, replacement.AssignmentState);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(CreateAssignment()));
    }

    [Fact]
    public async Task RuntimeDetach_ClearsPublishedIdentityWithoutChangingWorkerReadinessOrAssignment()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.Equal(WorkerAssignmentResult.Created, manager.Assign(CreateAssignment()));
        PodIdentity identity = new("runtime-ns", "runtime-pod", new("app", 42, "stamp"));
        Assert.True(manager.OnRuntimeAttached(1, identity));
        Assert.True(manager.OnRuntimeInitialized(1));
        WorkerPodState initialized = manager.State;
        Task<WorkerStatePollResult> poll = manager.WaitForChangeAsync(initialized.Revision);

        Assert.True(manager.OnRuntimeDetached(1));
        WorkerPodState detached = Assert.IsType<WorkerPodState>((await poll.WaitAsync(TestTimeout)).State);

        Assert.Equal(initialized.Revision + 1, detached.Revision);
        Assert.Null(detached.RuntimeIdentity);
        Assert.False(detached.RuntimeInitialized);
        Assert.True(detached.IsWorkerReady);
        Assert.Equal(initialized.SessionId, detached.SessionId);
        Assert.Equal(initialized.WorkerId, detached.WorkerId);
        Assert.Equal(WorkerAssignmentState.Ready, detached.AssignmentState);
        Assert.Equal(WorkerStatus.Starting, WorkerOriginState.FromState(detached).Status);
        Assert.Equal(WorkerLinkState.Unlinked, WorkerOriginState.FromState(detached).Link.State);
        Assert.False(manager.OnRuntimeDetached(1));
        Assert.Same(detached, manager.State);
        Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(CreateAssignment()));
    }

    [Fact]
    public void RuntimeDetach_StaleSessionCannotClearReplacementIdentity()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        Assert.True(manager.OnRuntimeAttached(1, new("runtime-ns", "old-runtime", new("app", 42, "stamp"))));
        Assert.True(manager.OnSessionTerminated(1));
        Assert.True(manager.OnWorkerAttached(2));
        Assert.True(manager.OnWorkerStartStream(2, "replacement-worker"));
        PodIdentity replacementIdentity = new("runtime-ns", "replacement-runtime", new("app", 42, "stamp"));
        Assert.True(manager.OnRuntimeAttached(2, replacementIdentity));
        WorkerPodState replacement = manager.State;

        Assert.False(manager.OnRuntimeDetached(1));

        Assert.Same(replacement, manager.State);
        Assert.Equal(replacementIdentity, manager.State.RuntimeIdentity);
        Assert.True(manager.State.IsWorkerReady);
    }

    [Fact]
    public void RuntimeOnlyTermination_PreventsLateWorkerAttachmentForThatSession()
    {
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
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
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
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
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
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
        IOptions<WorkerProxyOptions> options = CreateOptions(podName!);
        Assert.ThrowsAny<ArgumentException>(() =>
            new WorkerPodStateManager(options, TimeProvider.System));
    }

    [Fact]
    public void NullTimeProvider_IsRejected()
    {
        IOptions<WorkerProxyOptions> options = CreateOptions();
        Assert.Throws<ArgumentNullException>(() =>
            new WorkerPodStateManager(options, null!));
    }

    [Fact]
    public void NullOptions_IsRejected()
    {
        IOptions<WorkerProxyOptions> options = CreateOptions();
        Assert.Throws<ArgumentNullException>(() =>
            new WorkerPodStateManager(null!, TimeProvider.System));
    }

    [Fact]
    public void PodIdentity_IsCapturedDuringConstruction()
    {
        WorkerProxyOptions options = new() { PodName = "original-pod" };
        IOptions<WorkerProxyOptions> configuredOptions = Options.Create(options);
        WorkerPodStateManager manager = CreateManager(configuredOptions, TimeProvider.System);

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
        WorkerAssignmentResult[] results = await Task.WhenAll(attempts).WaitAsync(TestTimeout);

        Assert.Single(results, result => result == WorkerAssignmentResult.Created);
        Assert.Equal(31, results.Count(result => result == WorkerAssignmentResult.AlreadyAssigned));
        Assert.Equal(3, manager.State.Revision);
    }

    [Fact]
    public async Task ConcurrentConflictingAssignments_HaveOneWinningIdentity()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(WorkerAssignment Assignment, WorkerAssignmentResult Result)>[] attempts = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            WorkerAssignment assignment = CreateAssignment($"app-{index}", configVersion: index, contentVersion: index + 100);
            return (assignment, manager.Assign(assignment));
        })).ToArray();

        start.SetResult();
        (WorkerAssignment Assignment, WorkerAssignmentResult Result)[] results = await Task.WhenAll(attempts).WaitAsync(TestTimeout);
        (WorkerAssignment Assignment, WorkerAssignmentResult Result) winner = Assert.Single(results, result => result.Result == WorkerAssignmentResult.Created);

        Assert.Equal(winner.Assignment.FunctionAppName, manager.State.FunctionAppName);
        Assert.Equal(winner.Assignment.ConfigLastModifiedTime, manager.State.ConfigLastModifiedTime);
        Assert.Equal(winner.Assignment.ContentLastModifiedTime, manager.State.ContentLastModifiedTime);
        Assert.Equal(31, results.Count(result => result.Result == WorkerAssignmentResult.AssignmentConflict));
        Assert.Equal(3, manager.State.Revision);
    }

    [Fact]
    public async Task ConcurrentStartupModes_ClaimOneImmutableMode()
    {
        WorkerPodStateManager manager = CreateReadyManager();
        TaskCompletionSource start = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<(WorkerStartupMode Mode, WorkerAssignmentResult Result)>[] attempts = Enumerable.Range(0, 32).Select(index => Task.Run(async () =>
        {
            await start.Task;
            WorkerStartupMode mode = index % 2 == 0 ? WorkerStartupMode.Preconfigured : WorkerStartupMode.SpecializationRequired;
            return (mode, manager.Assign(CreateAssignment(startupMode: mode)));
        })).ToArray();

        start.SetResult();
        (WorkerStartupMode Mode, WorkerAssignmentResult Result)[] results = await Task.WhenAll(attempts).WaitAsync(TestTimeout);
        (WorkerStartupMode Mode, WorkerAssignmentResult Result) winner = Assert.Single(results, result => result.Result == WorkerAssignmentResult.Created);

        Assert.Equal(winner.Mode, manager.State.StartupMode);
        Assert.Equal(15, results.Count(result => result.Result == WorkerAssignmentResult.AlreadyAssigned));
        Assert.Equal(16, results.Count(result => result.Result == WorkerAssignmentResult.AssignmentConflict));
        Assert.All(results.Where(result => result.Mode != winner.Mode),
            result => Assert.Equal(WorkerAssignmentResult.AssignmentConflict, result.Result));
        Assert.Equal(3, manager.State.Revision);
    }

    [Fact]
    public async Task AssignmentRacingTermination_CannotLeaveWorkerReady()
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
            Assert.Equal(WorkerAssignmentResult.Created, result);
            Assert.False(manager.State.IsWorkerReady);
            if (manager.State.AssignmentState == WorkerAssignmentState.Terminated)
            {
                Assert.Equal(WorkerPodStatus.RemovalRequested, manager.State.PodStatus);
                Assert.Equal(WorkerStatus.RemovalRequested, WorkerOriginState.FromState(manager.State).Status);
                Assert.Equal(WorkerAssignmentState.Terminated, manager.State.AssignmentState);
                Assert.Equal(WorkerAssignmentResult.WorkerTerminated, manager.Assign(CreateAssignment()));
            }
            else
            {
                Assert.Equal(WorkerPodStatus.Starting, manager.State.PodStatus);
                Assert.Equal(WorkerStatus.Starting, WorkerOriginState.FromState(manager.State).Status);
                Assert.Equal(WorkerAssignmentState.Assigned, manager.State.AssignmentState);
                Assert.Equal(WorkerAssignmentResult.AlreadyAssigned, manager.Assign(CreateAssignment()));
            }

            Assert.Equal(4, manager.State.Revision);
        }
    }

    private static WorkerPodStateManager CreateManager(IOptions<WorkerProxyOptions> options, TimeProvider timeProvider)
        => new(options, timeProvider);

    private static WorkerPodStateManager CreateReadyManager()
    {
        WorkerPodStateManager manager = CreateManager(CreateOptions(), TimeProvider.System);
        manager.OnWorkerAttached(1);
        manager.OnWorkerStartStream(1, "worker");
        return manager;
    }

    private static WorkerAssignment CreateAssignment(
        string appName = "app",
        WorkerStartupMode startupMode = WorkerStartupMode.Preconfigured,
        string? httpProxyEndpoint = null, long? configVersion = null, long? contentVersion = null)
        => new(startupMode, appName, "http", false, new Dictionary<string, string> { ["SETTING"] = "value" }, "/home/site/wwwroot",
            httpProxyEndpoint: httpProxyEndpoint is null ? null : new Uri($"https://{httpProxyEndpoint}"),
            configLastModifiedTime: configVersion, contentLastModifiedTime: contentVersion);

    private static IOptions<WorkerProxyOptions> CreateOptions(string podName = "pod")
        => Options.Create(new WorkerProxyOptions { PodName = podName });
}
