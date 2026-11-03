// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Provides the infrastructure checkpoint used to poll a split worker pod.
/// </summary>
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
    private const string DiscoveryFunctionGroup = "deployment:validation";
    private static readonly SplitWorkerLink Unlinked = new(SplitWorkerLinkState.Unlinked);

    /// <summary>
    /// Projects a lifecycle snapshot without exposing assignment environment values or session details.
    /// </summary>
    public static SplitWorkerOriginState FromState(WorkerPodState state)
    {
        SplitWorkerStatus status = state.RemovalRequested ? SplitWorkerStatus.RemovalRequested
            : state.IsRuntimeInitialized ? SplitWorkerStatus.Running : SplitWorkerStatus.Starting;
        // Link describes the established relationship, even after removal withdraws live readiness.
        bool retainLink = state.RemovalRequested && state.RuntimeInitialized && state.RuntimeIdentity is not null;
        return new(
            ContainerType,
            new(
                "unknown",
                state.PodName,
                new("unknown", 0, "unknown")),
            IsDiscoveryPod: string.Equals(state.FunctionGroupName, DiscoveryFunctionGroup, StringComparison.Ordinal),
            IsAlwaysReady: state.IsAlwaysReady ?? false,
            FunctionGroup: state.FunctionGroupName,
            ConfigLastModifiedTime: state.ConfigLastModifiedTime,
            ContentLastModifiedTime: state.ContentLastModifiedTime,
            state.StartupTime,
            checked((int)state.Revision),
            status,
            state.IsRuntimeInitialized || retainLink ? new(SplitWorkerLinkState.Linked, state.RuntimeIdentity) : Unlinked,
            state.RemovalRequested || state.AssignmentState == WorkerAssignmentState.Failed);
    }
}
