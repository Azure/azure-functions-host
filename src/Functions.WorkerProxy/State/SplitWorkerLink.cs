// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

[JsonConverter(typeof(JsonStringEnumConverter<SplitWorkerLinkState>))]
internal enum SplitWorkerLinkState
{
    Unlinked = 0,
    Pending = 1,
    Linked = 2
}

[JsonConverter(typeof(JsonStringEnumConverter<SplitWorkerStatus>))]
internal enum SplitWorkerStatus
{
    Starting,
    ReadyForWork,
    Draining,
    RemovalRequested
}

internal sealed record SplitWorkerLink(
    SplitWorkerLinkState State,
    SplitPodIdentity? Runtime);

internal sealed record SplitFunctionAppIdentity(
    string FunctionAppName,
    int FunctionAppSiteId,
    string AntaresStampName);

internal sealed record SplitPodIdentity(
    string ResourceNamespace,
    string PodName,
    Guid PodGuid,
    string LegionStampName,
    string PodType,
    SplitFunctionAppIdentity App);

internal sealed record SplitWorkerStaticState(
    SplitPodIdentity Self,
    bool IsDiscoveryPod,
    bool IsAlwaysReady,
    string FunctionGroup,
    long ConfigLastModifiedTime,
    long ContentLastModifiedTime);

internal sealed record SplitWorkerDynamicState(
    long RevisionId,
    SplitWorkerStatus Status,
    SplitWorkerLink Link);

internal sealed record SplitWorkerOriginState(
    SplitWorkerStaticState StaticState,
    SplitWorkerDynamicState DynamicState)
{
    public string FunctionsContainerType => "FunctionsSplitWorkerProxy";

    public static SplitWorkerOriginState? FromState(WorkerPodState state)
    {
        if (state.SplitStaticState is not { } staticState || state.SplitLink is not { } link)
        {
            return null;
        }

        SplitWorkerStatus status = state.AssignmentState switch
        {
            WorkerAssignmentState.Failed => SplitWorkerStatus.RemovalRequested,
            WorkerAssignmentState.Ready when state.IsWorkerReady && link.State == SplitWorkerLinkState.Linked
                => SplitWorkerStatus.ReadyForWork,
            _ => SplitWorkerStatus.Starting
        };

        return new(staticState, new(state.Revision, status, link));
    }
}
