// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Provides an immutable lifecycle snapshot, without assignment environment values or wire-format concerns.
/// </summary>
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
    Uri? HttpProxyEndpoint = null)
{
    // WorkerId is recorded only after validated StartStream. Retaining it after termination does not imply readiness.
    public bool IsWorkerReady => IsWorkerAttached && WorkerId is not null;

    /// <summary>
    /// Creates an unassigned pod snapshot at revision zero with no worker attached.
    /// </summary>
    public static WorkerPodState CreateUnassigned(string podName) =>
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
            IsAlwaysReady: null);
}
