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
    /// Gets or sets the required WorkerProxy runtime-facing gRPC endpoint. Legion supplies
    /// an IPv4:port authority; absolute HTTP or HTTPS authorities remain accepted for compatibility.
    /// </summary>
    [JsonProperty("workerGrpcEndpoint")]
    public string? WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the Runtime access key supplied by the platform. It must not be logged or returned.
    /// </summary>
    [JsonProperty("runtimeAccessKey")]
    public string? RuntimeAccessKey { get; set; }

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
    [JsonProperty("resourceNamespace")]
    public string? ResourceNamespace { get; set; }

    [JsonProperty("podName")]
    public string? PodName { get; set; }

    [JsonProperty("podGuid")]
    public Guid? PodGuid { get; set; }

    [JsonProperty("legionStampName")]
    public string? LegionStampName { get; set; }

    [JsonProperty("podType")]
    public string? PodType { get; set; }

    [JsonProperty("app")]
    public FunctionsSplitFunctionAppIdentity? App { get; set; }
}

/// <summary>
/// Identifies the Function App assigned to a split pod.
/// </summary>
public sealed class FunctionsSplitFunctionAppIdentity
{
    [JsonProperty("functionAppName")]
    public string? FunctionAppName { get; set; }

    [JsonProperty("functionAppSiteId")]
    public int? FunctionAppSiteId { get; set; }

    [JsonProperty("antaresStampName")]
    public string? AntaresStampName { get; set; }
}
