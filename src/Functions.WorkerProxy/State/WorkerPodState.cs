// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Provides an immutable lifecycle snapshot, without assignment environment values or wire-format concerns.
/// </summary>
/// <param name="PodName">The worker proxy pod name.</param>
/// <param name="Revision">The monotonically increasing state revision.</param>
/// <param name="SessionId">The current or most recently terminated worker session identifier.</param>
/// <param name="IsWorkerAttached">Whether a worker stream is attached.</param>
/// <param name="WorkerId">The worker identity recorded after a validated StartStream.</param>
/// <param name="AssignmentState">The current assignment lifecycle state.</param>
/// <param name="StartupMode">The startup mode selected by the assignment.</param>
/// <param name="FunctionAppName">The assigned function app name.</param>
/// <param name="FunctionGroupName">The assigned function group name.</param>
/// <param name="IsAlwaysReady">Whether the assignment belongs to an always-ready allocation.</param>
/// <param name="StartupTime">The worker proxy startup time, retained across state revisions.</param>
/// <param name="MaxHttpConcurrency">The assigned maximum HTTP concurrency, when specified.</param>
internal sealed record WorkerPodState(
    string PodName,
    long Revision,
    long? SessionId,
    bool IsWorkerAttached,
    string? WorkerId,
    WorkerAssignmentState AssignmentState,
    WorkerStartupMode? StartupMode,
    string? FunctionAppName,
    string? FunctionGroupName,
    bool? IsAlwaysReady,
    DateTimeOffset StartupTime,
    int? MaxHttpConcurrency = null)
{
    // WorkerId is recorded only after validated StartStream. Retaining it after termination does not imply readiness.
    public bool IsWorkerReady => IsWorkerAttached && WorkerId is not null;

    // This is worker-pod eligibility, not proof of runtime initialization, function loading, or HTTP serving readiness.
    public WorkerPodStatus PodStatus => AssignmentState is WorkerAssignmentState.Failed or WorkerAssignmentState.Terminated
        ? WorkerPodStatus.RemovalRequested
        : AssignmentState == WorkerAssignmentState.Ready && IsWorkerReady
        ? WorkerPodStatus.Running
        : WorkerPodStatus.Starting;

    /// <summary>
    /// Creates an unassigned pod snapshot at revision zero with no worker attached.
    /// </summary>
    /// <param name="podName">The worker proxy pod name.</param>
    /// <param name="startupTime">The worker proxy startup time.</param>
    /// <returns>The initial pod snapshot.</returns>
    public static WorkerPodState CreateUnassigned(string podName, DateTimeOffset startupTime) =>
        new(
            PodName: podName,
            Revision: 0,
            SessionId: null,
            IsWorkerAttached: false,
            WorkerId: null,
            AssignmentState: WorkerAssignmentState.Unassigned,
            StartupMode: null,
            FunctionAppName: null,
            FunctionGroupName: null,
            IsAlwaysReady: null,
            StartupTime: startupTime);
}
