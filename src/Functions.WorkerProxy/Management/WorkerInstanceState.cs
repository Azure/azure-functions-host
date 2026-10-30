// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Functions.WorkerProxy.State;

namespace Azure.Functions.WorkerProxy.Management;

/// <summary>
/// Reports the split-worker origin state without fabricating platform-owned identity or application versions.
/// </summary>
internal sealed record WorkerInstanceState(
    [property: JsonPropertyOrder(-1)] string FunctionsContainerType,
    bool IsDiscoveryPod,
    bool IsAlwaysReady,
    string? FunctionGroup,
    DateTimeOffset StartupTime,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int RevisionId,
    string Status,
    WorkerLinkState? Link,
    bool IsUnhealthy)
{
    internal const string ContainerType = "FunctionsSplitWorkerProxy";

    // The current handshake provides no runtime identity or authenticated Link-acceptance acknowledgement.
    // Sending an app proof is not sufficient to advertise Running/Linked.
    public static WorkerInstanceState FromState(WorkerPodState state, DateTimeOffset startupTime) =>
        new(ContainerType, false, state.IsAlwaysReady ?? false, state.FunctionGroupName, startupTime,
            checked((int)state.Revision), "Starting", new("Unlinked"), state.AssignmentState is WorkerAssignmentState.Failed);

    public bool MatchesPublishedState(WorkerInstanceState other) =>
        RevisionId == other.RevisionId
        && StartupTime.Equals(other.StartupTime)
        && IsDiscoveryPod == other.IsDiscoveryPod
        && IsAlwaysReady == other.IsAlwaysReady
        && IsUnhealthy == other.IsUnhealthy
        && string.Equals(FunctionsContainerType, other.FunctionsContainerType, StringComparison.Ordinal)
        && string.Equals(FunctionGroup, other.FunctionGroup, StringComparison.Ordinal)
        && string.Equals(Status, other.Status, StringComparison.Ordinal)
        && Link is { } link && other.Link is { } otherLink
        && string.Equals(link.State, otherLink.State, StringComparison.Ordinal)
        && IsNullRuntime(link.Runtime) && IsNullRuntime(otherLink.Runtime);

    private static bool IsNullRuntime(JsonElement? runtime) =>
        runtime is null || runtime.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined;
}

internal sealed record WorkerLinkState(string State, [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] JsonElement? Runtime = null);
