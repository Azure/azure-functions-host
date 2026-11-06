// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Text.Json.Serialization;
using Azure.Functions.Rpc.Client;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Request body for linking a worker through <c>PUT /admin/workers/{workerId}</c>.
/// </summary>
public sealed class WorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the required scheme-less host:port of WorkerProxy's mapped runtime-facing
    /// gRPC listener. The runtime constructs HTTPS; an absolute HTTPS authority is also accepted for compatibility.
    /// The endpoint must be reachable from this runtime.
    /// </summary>
    [JsonPropertyName("workerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the target WorkerProxy's container encryption key. Never log or return this value.
    /// </summary>
    [JsonPropertyName("runtimeAccessKey")]
    public string? RuntimeAccessKey { get; set; }

    /// <summary>
    /// Gets or sets the platform's Runtime identity for Worker-state publication.
    /// The identity must be a nested JSON object; serialized JSON strings are not accepted.
    /// Omitted or null identities retain the conservative Starting/Unlinked signal.
    /// Unknown identity fields are ignored.
    /// </summary>
    [JsonPropertyName("runtimeIdentity")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WorkerLinkRuntimeIdentity? RuntimeIdentity { get; set; }

    /// <summary>
    /// Gets or sets the reserved worker-pod container encryption key. It is not used to authenticate
    /// the outbound connection and must not be logged or returned to the caller.
    /// </summary>
    [JsonPropertyName("workerContainerEncryptionKey")]
    public string? WorkerContainerEncryptionKey { get; set; }
}
