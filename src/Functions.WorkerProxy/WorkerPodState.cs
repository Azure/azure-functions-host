// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Pod status values for the worker proxy state machine.
/// Matches the Go Proxy's <c>FunctionAppPodStatus</c> pattern.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum WorkerPodStatus
{
    None,
    ReadyForRequest,
    Draining,
    DrainCompleted,
    MarkForDeletion
}

/// <summary>
/// Health status values for the worker proxy.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
internal enum WorkerPodHealthStatus
{
    None,
    Healthy,
    Unhealthy
}

/// <summary>
/// Change flags indicating what changed in the state (bitmask).
/// Same pattern as Go Proxy's <c>FunctionsPodStatusChangeFlags</c>.
/// </summary>
[Flags]
internal enum WorkerPodChangeFlags
{
    None = 0,
    PodStatus = 1,
    HealthStatus = 2
}

/// <summary>
/// State transition record for the worker pod.
/// </summary>
internal sealed class PodStatusTransition
{
    [JsonPropertyName("fromPodStatus")]
    public WorkerPodStatus FromPodStatus { get; set; }

    [JsonPropertyName("toPodStatus")]
    public WorkerPodStatus ToPodStatus { get; set; }
}

/// <summary>
/// Health status transition record.
/// </summary>
internal sealed class PodHealthStatusTransition
{
    [JsonPropertyName("fromPodStatus")]
    public WorkerPodHealthStatus FromPodStatus { get; set; }

    [JsonPropertyName("toPodStatus")]
    public WorkerPodHealthStatus ToPodStatus { get; set; }
}

/// <summary>
/// Worker pod state returned by the <c>/instanceState</c> endpoint.
/// Follows the same structure as the Go Proxy's <c>FunctionsPodState</c>.
/// </summary>
internal sealed class WorkerPodState
{
    [JsonPropertyName("currentPodStatusTransition")]
    public PodStatusTransition CurrentPodStatusTransition { get; set; } = new();

    [JsonPropertyName("currentPodHealthStatusTransition")]
    public PodHealthStatusTransition CurrentPodHealthStatusTransition { get; set; } = new();

    [JsonPropertyName("changeFlags")]
    public WorkerPodChangeFlags ChangeFlags { get; set; }

    [JsonPropertyName("revisionId")]
    public int RevisionId { get; set; }
}
