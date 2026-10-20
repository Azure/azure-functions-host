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
    ReadyForWork
}

internal sealed record SplitWorkerLink(
    [property: JsonRequired] SplitWorkerLinkState State);

internal sealed record SplitFunctionAppIdentity(
    [property: JsonRequired] string FunctionAppName,
    [property: JsonRequired] int FunctionAppSiteId,
    [property: JsonRequired] string AntaresStampName);

internal sealed record SplitPodIdentity(
    [property: JsonRequired] string ResourceNamespace,
    [property: JsonRequired] string PodName,
    [property: JsonRequired] Guid PodGuid,
    [property: JsonRequired] string LegionStampName,
    [property: JsonRequired] string PodType,
    [property: JsonRequired] SplitFunctionAppIdentity App);

internal sealed record SplitWorkerStaticState(
    [property: JsonRequired] SplitPodIdentity Self,
    [property: JsonRequired] bool IsDiscoveryPod,
    [property: JsonRequired] bool IsAlwaysReady,
    [property: JsonRequired]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    string? FunctionGroup,
    [property: JsonRequired] long ConfigLastModifiedTime,
    [property: JsonRequired] long ContentLastModifiedTime)
{
    internal const string PodType = "FunctionsSplitWorkerPod";
    private const string PlaceholderIdentity = "unassigned";

    public static SplitWorkerStaticState CreatePlaceholder(string podName) =>
        new(
            new(
                PlaceholderIdentity,
                podName,
                Guid.Empty,
                PlaceholderIdentity,
                PodType,
                new(PlaceholderIdentity, 0, PlaceholderIdentity)),
            IsDiscoveryPod: false,
            IsAlwaysReady: false,
            FunctionGroup: null,
            ConfigLastModifiedTime: 0,
            ContentLastModifiedTime: 0);
}

internal sealed record SplitWorkerDynamicState(
    [property: JsonRequired] long RevisionId,
    [property: JsonRequired] SplitWorkerStatus Status,
    [property: JsonRequired] SplitWorkerLink Link);

internal sealed record SplitWorkerOriginState(
    [property: JsonRequired] string FunctionsContainerType,
    [property: JsonRequired] SplitWorkerStaticState StaticState,
    [property: JsonRequired] SplitWorkerDynamicState DynamicState)
{
    private const string ContainerType = "FunctionsSplitWorkerProxy";

    public static SplitWorkerOriginState FromState(WorkerPodState state)
    {
        SplitWorkerStatus status =
            state.AssignmentState == WorkerAssignmentState.Ready
            && state.IsWorkerReady
            && state.SplitLink.State == SplitWorkerLinkState.Linked
                ? SplitWorkerStatus.ReadyForWork
                : SplitWorkerStatus.Starting;

        return new(ContainerType, state.SplitStaticState, new(state.Revision, status, state.SplitLink));
    }
}
