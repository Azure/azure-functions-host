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
    [property: JsonRequired, JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FunctionGroup,
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

    public static SplitWorkerOriginState FromState(WorkerPodState state) =>
        new(
            ContainerType,
            new(
                "unknown",
                state.PodName,
                Guid.Empty,
                "unknown",
                "FunctionsSplitWorkerPod",
                new("unknown", 0, "unknown")),
            IsDiscoveryPod: false,
            IsAlwaysReady: state.IsAlwaysReady ?? false,
            FunctionGroup: state.FunctionGroupName,
            ConfigLastModifiedTime: 0,
            ContentLastModifiedTime: 0,
            state.StartupTime,
            checked((int)state.Revision),
            state.RemovalRequested ? SplitWorkerStatus.RemovalRequested : SplitWorkerStatus.Starting,
            Unlinked,
            state.AssignmentState == WorkerAssignmentState.Failed);
}
