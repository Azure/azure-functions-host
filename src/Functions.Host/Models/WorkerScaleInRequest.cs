// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerScaleIn;

/// <summary>
/// Requests one count-only non-HTTP worker scale-in decision.
/// </summary>
public sealed class WorkerScaleInRequest
{
    /// <summary>
    /// Gets or sets the exact trusted non-HTTP function group.
    /// </summary>
    [JsonProperty("functionGroup")]
    public string? FunctionGroup { get; set; }

    /// <summary>
    /// Gets or sets the exact always-ready capacity class.
    /// </summary>
    [JsonProperty("isAlwaysReady")]
    public bool? IsAlwaysReady { get; set; }

    /// <summary>
    /// Gets or sets the absolute number of matching usable workers to retain.
    /// </summary>
    [JsonProperty("desiredWorkerCount")]
    public int? DesiredWorkerCount { get; set; }
}

/// <summary>
/// Reports how many workers this request newly committed to drain.
/// </summary>
/// <param name="NewlyDrainingWorkerCount">The newly committed worker drain count.</param>
public sealed record WorkerScaleInResponse(
    [property: JsonProperty("newlyDrainingWorkerCount")]
    int NewlyDrainingWorkerCount);
