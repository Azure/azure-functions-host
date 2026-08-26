// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates raw FunctionRpc client connections.
/// </summary>
internal interface IRpcClientConnectionFactory
{
    /// <summary>
    /// Creates a connection using validated options.
    /// </summary>
    /// <param name="options">The connection options.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected FunctionRpc client connection.</returns>
    Task<RpcClientConnection> ConnectAsync(RpcClientConnectionOptions options, CancellationToken cancellationToken = default);
}
