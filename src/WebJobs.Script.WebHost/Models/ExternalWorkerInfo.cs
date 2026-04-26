// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models;

/// <summary>
/// Request body for <c>PUT /admin/workers/{workerId}</c>.
/// Property names match Legion's <c>WorkerLinkInfo</c> record
/// (PascalCase, System.Text.Json default serialization).
/// </summary>
public sealed class ExternalWorkerInfo
{
    /// <summary>
    /// Gets or sets the worker pod's name as known to the platform.
    /// </summary>
    [JsonProperty("WorkerPodName")]
    public string WorkerPodName { get; set; }

    /// <summary>
    /// Gets or sets the HTTP endpoint URI of the worker proxy.
    /// </summary>
    [JsonProperty("WorkerHttpEndpoint")]
    public string WorkerHttpEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the gRPC endpoint URI of the worker proxy (required).
    /// </summary>
    /// <example>http://10.0.1.42:50051.</example>
    [JsonProperty("WorkerGrpcEndpoint")]
    public string WorkerGrpcEndpoint { get; set; }

    /// <summary>
    /// Gets or sets the authentication key for the worker proxy connection.
    /// Used by the runtime to authenticate outbound gRPC connections to the worker proxy.
    /// </summary>
    [JsonProperty("WorkerContainerEncryptionKey")]
    public string WorkerContainerEncryptionKey { get; set; }
}
