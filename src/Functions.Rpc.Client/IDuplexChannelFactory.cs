// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates connected duplex channels.
/// </summary>
/// <remarks>
/// A returned channel that owns transport resources must also implement <see cref="System.IAsyncDisposable"/>.
/// The caller owns the returned channel.
/// </remarks>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal interface IDuplexChannelFactory<T>
    where T : class
{
    /// <summary>
    /// Connects to an endpoint and creates a duplex channel.
    /// </summary>
    /// <remarks>
    /// Implementations backed by a cached transport can surface a later reconnection failure through the returned
    /// channel rather than from this method.
    /// </remarks>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected duplex channel.</returns>
    Task<Channel<T>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
}
