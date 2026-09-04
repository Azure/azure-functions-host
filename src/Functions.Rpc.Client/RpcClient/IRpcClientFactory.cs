// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates lightweight FunctionRpc clients over reusable gRPC channels.
/// </summary>
/// <remarks>
/// The factory owns its cached channels and must outlive every client and call created from them.
/// Dispose calls before disposing the factory.
/// </remarks>
internal interface IRpcClientFactory : IAsyncDisposable
{
    /// <summary>
    /// Gets a client for an endpoint, connecting and caching its channel when first requested.
    /// </summary>
    /// <remarks>
    /// A cached channel is returned without a new readiness check. A later call observes and reports any transport
    /// reconnection failure.
    /// </remarks>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="cancellationToken">A token that cancels connection establishment or waiting for an existing connection.</param>
    /// <returns>The FunctionRpc client.</returns>
    ValueTask<FunctionRpc.FunctionRpcClient> CreateAsync(Uri endpoint, CancellationToken cancellationToken = default);
}
