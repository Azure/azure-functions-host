// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates connected duplex channels.
/// </summary>
/// <remarks>The caller owns the returned channel.</remarks>
/// <typeparam name="T">The message type used in both directions.</typeparam>
internal interface IDuplexChannelFactory<T>
    where T : class
{
    /// <summary>
    /// Connects to an endpoint and creates a duplex channel.
    /// </summary>
    /// <remarks>
    /// Implementations backed by a cached connection can surface a later reconnection failure through the returned
    /// channel rather than from this method.
    /// </remarks>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected duplex channel.</returns>
    Task<DuplexChannel<T>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
        => ConnectAsync(endpoint, new Dictionary<string, string>(), runtimeIdentity: null, cancellationToken);

    /// <summary>
    /// Connects with the platform's serialized Runtime identity.
    /// </summary>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="runtimeIdentity">Optional serialized identity forwarded as binary gRPC metadata.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected duplex channel.</returns>
    Task<DuplexChannel<T>> ConnectAsync(Uri endpoint, string runtimeIdentity, CancellationToken cancellationToken = default)
        => ConnectAsync(endpoint, new Dictionary<string, string>(), runtimeIdentity, cancellationToken);

    /// <summary>
    /// Connects with caller authentication headers.
    /// </summary>
    Task<DuplexChannel<T>> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken = default)
        => ConnectAsync(endpoint, headers, runtimeIdentity: null, cancellationToken);

    /// <summary>
    /// Connects with caller authentication headers and the platform's serialized Runtime identity.
    /// </summary>
    Task<DuplexChannel<T>> ConnectAsync(
        Uri endpoint,
        IReadOnlyDictionary<string, string> headers,
        string runtimeIdentity,
        CancellationToken cancellationToken = default);
}
