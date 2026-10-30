// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Legacy Legion request body for linking a worker through <c>PUT /admin/workers</c>.
/// </summary>
public sealed class LegacyWorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the required worker pod name used as the linked channel identity.
    /// </summary>
    [JsonProperty("WorkerPodName")]
    public string? WorkerPodName { get; set; }

    /// <summary>
    /// Gets or sets the required HTTP or HTTPS authority of WorkerProxy's runtime-facing gRPC listener.
    /// </summary>
    [JsonProperty("WorkerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the legacy Runtime access key, reserved for wire compatibility.
    /// It is not used to authenticate the outbound connection and must not be logged or returned.
    /// </summary>
    [JsonProperty("RuntimeAccessKey")]
    public string? RuntimeAccessKey { get; set; }
}
