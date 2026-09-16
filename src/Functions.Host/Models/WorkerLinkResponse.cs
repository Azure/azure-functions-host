// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Response body for <c>PUT /admin/workers</c>. Reports the outcome of a worker link attempt.
/// </summary>
/// <remarks>
/// A successful link returns HTTP 200 after the outbound connection, StartStream,
/// initialization handshake, and channel registration complete. This does not imply
/// invocation readiness or ScriptHost startup.
/// </remarks>
public sealed class WorkerLinkResponse
{
    /// <summary>
    /// Gets or sets the worker pod name echoed from the request, for caller correlation.
    /// </summary>
    [JsonProperty("workerPodName")]
    public string? WorkerPodName { get; set; }

    /// <summary>
    /// Gets or sets the link outcome. See <see cref="WorkerLinkStatus"/>.
    /// </summary>
    [JsonProperty("status")]
    [JsonConverter(typeof(StringEnumConverter))]
    public WorkerLinkStatus Status { get; set; }

    /// <summary>
    /// Gets or sets an optional human-readable detail, primarily for diagnostics and rejection reasons.
    /// </summary>
    [JsonProperty("detail", NullValueHandling = NullValueHandling.Ignore)]
    public string? Detail { get; set; }
}
