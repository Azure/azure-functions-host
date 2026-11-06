// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json.Serialization;

namespace Azure.Functions.WorkerProxy.State;

/// <summary>
/// Exposes the worker proxy checkpoint consumed by the platform's infrastructure instance-state endpoint.
/// </summary>
/// <param name="FunctionsContainerType">The platform discriminator for the container kind.</param>
/// <param name="Self">The trusted pod name, with unknown placeholders for platform-owned namespace and app identity.</param>
/// <param name="IsDiscoveryPod">Whether the pod is used for discovery.</param>
/// <param name="IsAlwaysReady">Whether the pod belongs to an always-ready allocation.</param>
/// <param name="FunctionGroup">The assigned function group, or <see langword="null"/> before assignment.</param>
/// <param name="ConfigLastModifiedTime">Zero until confirmed loaded configuration versions are integrated.</param>
/// <param name="ContentLastModifiedTime">Zero until confirmed loaded content versions are integrated.</param>
/// <param name="StartupTime">The worker proxy startup time.</param>
/// <param name="RevisionId">The revision of this checkpoint.</param>
/// <param name="Status">The platform-visible worker proxy lifecycle status.</param>
/// <param name="Link">The worker proxy's runtime link state.</param>
/// <param name="IsUnhealthy">Whether known authentication configuration or an assigned session has failed.</param>
internal sealed record WorkerOriginState(
    [property: JsonRequired, JsonPropertyOrder(-1)] string FunctionsContainerType,
    [property: JsonRequired] PodIdentity Self,
    bool IsDiscoveryPod,
    [property: JsonRequired] bool IsAlwaysReady,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.Never)] string? FunctionGroup,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ConfigLastModifiedTime,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long ContentLastModifiedTime,
    [property: JsonRequired] DateTimeOffset StartupTime,
    [property: JsonRequired, JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] int RevisionId,
    [property: JsonRequired] WorkerStatus Status,
    [property: JsonRequired] WorkerLink Link,
    [property: JsonRequired] bool IsUnhealthy)
{
    /// <summary>
    /// Identifies worker proxy checkpoints in the platform's instance-state contract.
    /// </summary>
    // Legion's IFunctionsPodOriginState requires this exact functionsContainerType discriminator for polymorphic deserialization.
    public const string ContainerType = "FunctionsSplitWorkerProxy";
    private static readonly WorkerLink Unlinked = new(WorkerLinkState.Unlinked);

    /// <summary>
    /// Projects a pod snapshot into the platform checkpoint, including the current runtime link and lifecycle status.
    /// </summary>
    /// <param name="state">The current immutable pod snapshot.</param>
    /// <returns>The corresponding platform checkpoint.</returns>
    public static WorkerOriginState FromState(WorkerPodState state)
    {
        WorkerStatus status = state.PodStatus == WorkerPodStatus.RemovalRequested
            ? WorkerStatus.RemovalRequested
            : state.IsRuntimeInitialized ? WorkerStatus.Running : WorkerStatus.Starting;

        return new(
            ContainerType,
            new("unknown", state.PodName, new("unknown", 0, "unknown")),
            IsDiscoveryPod: false,
            IsAlwaysReady: state.IsAlwaysReady ?? false,
            FunctionGroup: state.FunctionGroupName,
            ConfigLastModifiedTime: 0,
            ContentLastModifiedTime: 0,
            state.StartupTime,
            checked((int)state.Revision),
            status,
            state.IsRuntimeInitialized ? new(WorkerLinkState.Linked, state.RuntimeIdentity) : Unlinked,
            state.IsUnhealthy);
    }
}
