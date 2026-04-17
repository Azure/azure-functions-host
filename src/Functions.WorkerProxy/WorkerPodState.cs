// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;

namespace Microsoft.Azure.Functions.WorkerProxy;

/// <summary>
/// Pod status values for the worker proxy state machine.
/// Workers use only <c>ReadyForRequest</c>, <c>Draining</c>, and <c>MarkedForDeletion</c>
/// in signal state. Workers do not use <c>MarkedForStop</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkerPodStatus>))]
internal enum WorkerPodStatus
{
    None,
    ReadyForRequest,
    Draining,
    MarkedForDeletion
}

/// <summary>
/// Drain reason values accepted by <c>POST /admin/worker/drain</c>.
/// The first accepted reason is persisted for the lifetime of the worker pod.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DrainReason>))]
internal enum DrainReason
{
    IdleScaleIn,
    RuntimeStopping,
    ReplaceWorkerKeepRuntime,
    OrphanCleanup
}

/// <summary>
/// Replacement policy derived from the accepted drain reason.
/// Once first published in <c>Draining</c>, the value remains unchanged through <c>MarkedForDeletion</c>.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ReplacementPolicy>))]
internal enum ReplacementPolicy
{
    NoReplacement,
    SameRuntimeRefill
}

/// <summary>
/// Worker instance state returned by <c>POST /admin/infra/instanceState</c>.
/// Matches the platform-facing <c>FunctionsWorkerPod</c> schema defined in the Goal 3 design doc.
/// </summary>
internal sealed class WorkerInstanceState
{
    [JsonPropertyName("functionsContainerType")]
    public string FunctionsContainerType { get; set; } = "FunctionsWorkerPod";

    [JsonPropertyName("podName")]
    public string PodName { get; set; } = string.Empty;

    [JsonPropertyName("revision")]
    public int Revision { get; set; }

    [JsonPropertyName("state")]
    public WorkerInstanceStateDetails State { get; set; } = new();
}

/// <summary>
/// Nested state details within the <see cref="WorkerInstanceState"/> response.
/// </summary>
internal sealed class WorkerInstanceStateDetails
{
    [JsonPropertyName("podStatus")]
    public WorkerPodStatus PodStatus { get; set; }

    [JsonPropertyName("runtimePodName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RuntimePodName { get; set; }

    [JsonPropertyName("functionGroupName")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FunctionGroupName { get; set; }

    [JsonPropertyName("isAlwaysReady")]
    public bool IsAlwaysReady { get; set; }

    [JsonPropertyName("replacementPolicy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReplacementPolicy? ReplacementPolicy { get; set; }
}

/// <summary>
/// Request payload for <c>POST /admin/worker/drain</c>.
/// </summary>
internal sealed class WorkerDrainRequest
{
    public DrainReason Reason { get; set; }
}

/// <summary>
/// Request payload for <c>POST /admin/infra/instanceState</c> polling.
/// Contains the client's last known revision so the pod can long-poll until a change occurs.
/// </summary>
internal sealed class InstanceStatePollRequest
{
    public int Revision { get; set; }
}
