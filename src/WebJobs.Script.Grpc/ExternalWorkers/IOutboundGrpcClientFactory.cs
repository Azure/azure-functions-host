// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Factory for creating <see cref="IOutboundGrpcClient"/> instances.
/// Enables unit testing of <see cref="WorkerConnectionService"/> without real gRPC connections.
/// </summary>
internal interface IOutboundGrpcClientFactory
{
    /// <summary>
    /// Creates a new <see cref="IOutboundGrpcClient"/>.
    /// </summary>
    IOutboundGrpcClient Create();
}
