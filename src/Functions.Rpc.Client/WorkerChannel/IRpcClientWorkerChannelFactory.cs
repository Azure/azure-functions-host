// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates client-backed worker channels.
/// </summary>
internal interface IRpcClientWorkerChannelFactory
{
    /// <summary>
    /// Creates a worker channel without credentials.
    /// </summary>
    /// <remarks>This compatibility seam is retained only for existing test doubles.</remarks>
    RpcClientWorkerChannel Create(
        string workerId,
        DuplexChannel<StreamingMessage> ownedChannel)
        => throw new NotSupportedException("Runtime credentials are required for client-backed worker channels.");

    /// <summary>
    /// Creates a worker channel and takes ownership of <paramref name="ownedChannel"/> when this method succeeds.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="ownedChannel">The connected duplex transport.</param>
    /// <param name="credentials">The admitted worker's runtime credentials.</param>
    /// <returns>The client-backed worker channel.</returns>
    RpcClientWorkerChannel Create(
        string workerId,
        DuplexChannel<StreamingMessage> ownedChannel,
        RuntimeWorkerCredentials credentials);
}
