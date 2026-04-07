// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Grpc.ExternalWorkers;

/// <summary>
/// Represents an outbound gRPC client that connects to an external worker sidecar
/// and runs bidirectional message pumps.
/// </summary>
internal interface IOutboundGrpcClient : IAsyncDisposable
{
    /// <summary>
    /// Connects to the remote gRPC endpoint and starts the bidirectional message pump.
    /// </summary>
    /// <param name="workerId">The worker identifier whose channels have been pre-registered.</param>
    /// <param name="endpoint">The URI of the remote <c>FunctionRpc</c> service.</param>
    /// <param name="cancellationToken">Token to cancel the connection attempt.</param>
    Task ConnectAsync(string workerId, Uri endpoint, CancellationToken cancellationToken);
}
