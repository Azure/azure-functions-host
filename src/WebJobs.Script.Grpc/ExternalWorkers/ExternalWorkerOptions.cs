// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Configuration options for connecting to external worker processes.
/// </summary>
internal class ExternalWorkerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the external worker feature is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the gRPC endpoint URI of the external worker to connect to.
    /// When set, the host will initiate an outbound gRPC connection to this endpoint on startup.
    /// </summary>
    public string? GrpcEndpoint { get; set; }
}
