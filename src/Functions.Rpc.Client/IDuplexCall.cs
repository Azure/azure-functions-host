// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Represents the request and response streams of one duplex call.
/// </summary>
/// <remarks>
/// <see cref="IAsyncDisposable.DisposeAsync"/> aborts the call and must unblock concurrent
/// <see cref="WriteAsync"/> and <see cref="ReadAllAsync"/> operations. Graceful request-stream half-close is not part of
/// this raw transport contract.
/// </remarks>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal interface IDuplexCall<in TRequest, out TResponse> : IAsyncDisposable
    where TRequest : class
    where TResponse : class
{
    /// <summary>
    /// Writes one request to the call.
    /// </summary>
    /// <param name="request">The request to write.</param>
    /// <returns>A task representing the write.</returns>
    Task WriteAsync(TRequest request);

    /// <summary>
    /// Asynchronously enumerates responses from the call.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels response enumeration.</param>
    /// <returns>The asynchronous response sequence.</returns>
    IAsyncEnumerable<TResponse> ReadAllAsync(CancellationToken cancellationToken = default);
}
