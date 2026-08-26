// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Creates connected duplex calls.
/// </summary>
/// <typeparam name="TRequest">The request type.</typeparam>
/// <typeparam name="TResponse">The response type.</typeparam>
internal interface IDuplexCallFactory<TRequest, TResponse>
    where TRequest : class
    where TResponse : class
{
    /// <summary>
    /// Connects to an endpoint and creates a duplex call.
    /// </summary>
    /// <param name="endpoint">The service endpoint.</param>
    /// <param name="cancellationToken">A token that bounds connection establishment.</param>
    /// <returns>The connected duplex call.</returns>
    Task<IDuplexCall<TRequest, TResponse>> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
}
