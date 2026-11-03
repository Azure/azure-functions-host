// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

internal sealed record SplitWorkerOriginState(
    [property: JsonRequired] string FunctionsContainerType,
    [property: JsonRequired] SplitPodIdentity Self,
    [property: JsonRequired] bool IsDiscoveryPod,
    [property: JsonRequired] bool IsAlwaysReady,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FunctionGroup,
    [property: JsonRequired] long ConfigLastModifiedTime,
    [property: JsonRequired] long ContentLastModifiedTime,
    [property: JsonRequired] DateTimeOffset StartupTime,
    [property: JsonRequired] int RevisionId,
    [property: JsonRequired] SplitWorkerStatus Status,
    [property: JsonRequired] SplitWorkerLink Link,
    [property: JsonRequired] bool IsUnhealthy)
{
    public const string ContainerType = "FunctionsSplitWorkerProxy";
    private static readonly SplitWorkerLink Unlinked = new(SplitWorkerLinkState.Unlinked);

    public static SplitWorkerOriginState FromState(WorkerPodState state)
    {
        SplitWorkerStatus status = state.IsRuntimeInitialized
            ? SplitWorkerStatus.Running
            : SplitWorkerStatus.Starting;
        return new(
            ContainerType,
            new(
                "unknown",
                state.PodName,
                new("unknown", 0, "unknown")),
            IsDiscoveryPod: string.Equals(state.FunctionGroupName, "deployment:validation", StringComparison.Ordinal),
            IsAlwaysReady: state.IsAlwaysReady ?? false,
            FunctionGroup: state.FunctionGroupName,
            ConfigLastModifiedTime: state.ConfigLastModifiedTime,
            ContentLastModifiedTime: state.ContentLastModifiedTime,
            state.StartupTime,
            checked((int)state.Revision),
            status,
            state.IsRuntimeInitialized ? new(SplitWorkerLinkState.Linked, state.RuntimeIdentity) : Unlinked,
            state.AssignmentState == WorkerAssignmentState.Failed);
    }
}
