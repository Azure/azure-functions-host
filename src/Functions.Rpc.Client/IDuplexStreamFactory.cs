// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates connected duplex streams.
/// </summary>
/// <remarks>
/// A returned stream that owns transport resources must also implement <see cref="System.IAsyncDisposable"/>.
/// The caller owns the returned stream.
/// </remarks>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal interface IDuplexStreamFactory<T>
    where T : class
{
    /// <summary>
    /// Connects to an endpoint and creates a duplex stream.
    /// </summary>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected duplex stream.</returns>
    Task<Channel<T>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
}
