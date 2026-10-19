// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Newtonsoft.Json;

namespace Azure.Functions.Host.WorkerLink;

/// <summary>
/// Request body for linking a worker through <c>PUT /admin/workers/{workerId}</c>.
/// </summary>
public sealed class WorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the required HTTP or HTTPS authority of WorkerProxy's runtime-facing
    /// gRPC listener. The endpoint must be reachable from this runtime.
    /// </summary>
    [JsonProperty("workerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the reserved worker-pod container encryption key. It is not used to authenticate
    /// the outbound connection and must not be logged or returned to the caller.
    /// </summary>
    [JsonProperty("workerContainerEncryptionKey")]
    public string? WorkerContainerEncryptionKey { get; set; }

    /// <summary>
    /// Gets or sets the Runtime identity acknowledged to WorkerProxy after Host linking succeeds.
    /// </summary>
    [JsonProperty("runtime")]
    public FunctionsSplitPodIdentity? Runtime { get; set; }
}

/// <summary>
/// Identifies one split Runtime pod.
/// </summary>
public sealed class FunctionsSplitPodIdentity
{
    /// <summary>Gets or sets the resource namespace.</summary>
    [JsonProperty("resourceNamespace")]
    public string? ResourceNamespace { get; set; }

    /// <summary>Gets or sets the pod name.</summary>
    [JsonProperty("podName")]
    public string? PodName { get; set; }

    /// <summary>Gets or sets the pod GUID.</summary>
    [JsonProperty("podGuid")]
    public Guid? PodGuid { get; set; }

    /// <summary>Gets or sets the Legion stamp name.</summary>
    [JsonProperty("legionStampName")]
    public string? LegionStampName { get; set; }

    /// <summary>Gets or sets the Functions pod type.</summary>
    [JsonProperty("podType")]
    public string? PodType { get; set; }

    /// <summary>Gets or sets the Function App identity.</summary>
    [JsonProperty("app")]
    public FunctionsSplitFunctionAppIdentity? App { get; set; }
}

/// <summary>
/// Identifies the Function App assigned to a split pod.
/// </summary>
public sealed class FunctionsSplitFunctionAppIdentity
{
    /// <summary>Gets or sets the Function App name.</summary>
    [JsonProperty("functionAppName")]
    public string? FunctionAppName { get; set; }

    /// <summary>Gets or sets the Function App site ID.</summary>
    [JsonProperty("functionAppSiteId")]
    public int? FunctionAppSiteId { get; set; }

    /// <summary>Gets or sets the Antares stamp name.</summary>
    [JsonProperty("antaresStampName")]
    public string? AntaresStampName { get; set; }
}
