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

    public static SplitWorkerOriginState FromState(WorkerPodState state)
    {
        SplitWorkerStaticState staticState = state.SplitStaticState;
        SplitWorkerStatus status =
            state.AssignmentState == WorkerAssignmentState.Ready
            && state.IsWorkerReady
            && state.IsRuntimeLinkAcknowledged
            && state.SplitLink is { State: SplitWorkerLinkState.Linked, Runtime: not null }
                ? SplitWorkerStatus.Running
                : SplitWorkerStatus.Starting;

        return new(
            ContainerType,
            staticState.Self,
            staticState.IsDiscoveryPod,
            staticState.IsAlwaysReady,
            staticState.FunctionGroup,
            staticState.ConfigLastModifiedTime,
            staticState.ContentLastModifiedTime,
            staticState.StartupTime,
            checked((int)state.Revision),
            status,
            state.SplitLink,
            state.AssignmentState == WorkerAssignmentState.Failed);
    }
}
