// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models;

/// <summary>
/// Request body for <c>POST /admin/workers/link</c>.
/// </summary>
public sealed class WorkerLinkRequest
{
    /// <summary>
    /// Gets or sets the platform-assigned worker identifier.
    /// If null, the host generates one.
    /// </summary>
    [JsonProperty("workerId")]
    public string WorkerId { get; set; }

    /// <summary>
    /// Gets or sets the worker pod's name as known to the platform.
    /// </summary>
    [JsonProperty("podName")]
    public string PodName { get; set; }

    /// <summary>
    /// Gets or sets the gRPC endpoint URI of the worker proxy (required).
    /// </summary>
    /// <example>http://10.0.1.42:50051</example>
    [JsonProperty("grpcEndpoint")]
    public string GrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the authentication key for the worker proxy connection.
    /// Used by the runtime to authenticate outbound gRPC connections to the worker proxy.
    /// </summary>
    [JsonProperty("podKey")]
    public string PodKey { get; set; }
}
