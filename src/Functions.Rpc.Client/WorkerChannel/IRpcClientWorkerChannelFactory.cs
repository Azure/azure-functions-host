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
    /// Creates a worker channel and takes ownership of <paramref name="ownedChannel"/> when this method succeeds.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="ownedChannel">The connected duplex transport.</param>
    /// <param name="httpEndpoint">The platform-provided HTTP invocation endpoint, or <see langword="null"/> to disable HTTP invocations.</param>
    /// <returns>The client-backed worker channel.</returns>
    RpcClientWorkerChannel Create(
        string workerId,
        DuplexChannel<StreamingMessage> ownedChannel,
        Uri httpEndpoint = null);
}
