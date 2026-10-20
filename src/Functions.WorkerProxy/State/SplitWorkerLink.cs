// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

[JsonConverter(typeof(JsonStringEnumConverter<SplitWorkerLinkState>))]
internal enum SplitWorkerLinkState
{
    Unlinked = 0,
    Linked = 1
}

[JsonConverter(typeof(JsonStringEnumConverter<SplitWorkerStatus>))]
internal enum SplitWorkerStatus
{
    Starting,
    ReadyForWork,
    Draining,
    RemovalRequested
}

internal sealed record SplitWorkerLink(SplitWorkerLinkState State);

internal sealed record SplitFunctionAppIdentity(
    string FunctionAppName,
    [property: JsonRequired] int FunctionAppSiteId,
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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FunctionGroup,
    long ConfigLastModifiedTime,
    long ContentLastModifiedTime)
{
    private const string PlaceholderIdentity = "unassigned";

    public static SplitWorkerStaticState CreatePlaceholder(string podName) =>
        new(
            new(
                PlaceholderIdentity,
                podName,
                Guid.Empty,
                PlaceholderIdentity,
                SplitPodTypes.Worker,
                new(PlaceholderIdentity, 0, PlaceholderIdentity)),
            IsDiscoveryPod: false,
            IsAlwaysReady: false,
            FunctionGroup: null,
            ConfigLastModifiedTime: 0,
            ContentLastModifiedTime: 0);
}

internal sealed record SplitWorkerDynamicState(
    long RevisionId,
    SplitWorkerStatus Status,
    SplitWorkerLink Link);

internal sealed record SplitWorkerOriginState(
    string FunctionsContainerType,
    SplitWorkerStaticState StaticState,
    SplitWorkerDynamicState DynamicState)
{
    private const string ContainerType = "FunctionsSplitWorkerProxy";

    public static SplitWorkerOriginState FromState(WorkerPodState state)
    {
        if (state.SplitStaticState is not { } staticState || state.SplitLink is not { } link)
        {
            throw new InvalidOperationException("WorkerProxy state must always contain split Worker signal state.");
        }

        SplitWorkerStatus status =
            state.AssignmentState == WorkerAssignmentState.Ready
            && state.IsWorkerReady
            && link.State == SplitWorkerLinkState.Linked
                ? SplitWorkerStatus.ReadyForWork
                : SplitWorkerStatus.Starting;

        return new(ContainerType, staticState, new(state.Revision, status, link));
    }
}
