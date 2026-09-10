// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Request body for linking a worker pod through <c>PUT /admin/workers</c>.
/// </summary>
public sealed class WorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the required worker pod name used as the link's correlation key.
    /// Compared using ordinal equality and kept unchanged across retries; it does not
    /// replace the language worker's FunctionRpc <c>worker_id</c>.
    /// </summary>
    [JsonProperty("workerPodName")]
    public string? WorkerPodName { get; set; }

    /// <summary>
    /// Gets or sets the required HTTP or HTTPS authority of WorkerProxy's runtime-facing
    /// gRPC listener. The endpoint must be reachable from this runtime.
    /// </summary>
    [JsonProperty("workerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the reserved HTTP invocation endpoint.
    /// Validated when nonblank, but not used by the gRPC link operation.
    /// </summary>
    [JsonProperty("workerHttpEndpoint")]
    public string? WorkerHttpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the reserved worker-pod container encryption key. It is not used to authenticate
    /// the outbound connection and must not be logged or returned to the caller.
    /// </summary>
    [JsonProperty("workerContainerEncryptionKey")]
    public string? WorkerContainerEncryptionKey { get; set; }
}
