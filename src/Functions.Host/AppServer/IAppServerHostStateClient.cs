// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;

namespace Azure.Functions.Host.AppServer;

/// <summary>
/// Sends Host worker-count snapshots to the co-located AppServer.
/// </summary>
internal interface IAppServerHostStateClient
{
    /// <summary>
    /// Gets a value indicating whether the key that signs AppServer requests is available.
    /// </summary>
    /// <remarks>The key is available only after the Host is specialized to a site.</remarks>
    bool HasSigningKey { get; }

    /// <summary>
    /// Sends one snapshot.
    /// </summary>
    /// <param name="state">The snapshot to send.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The AppServer response status code.</returns>
    Task<HttpStatusCode> PublishAsync(ComputeRuntimeState state, CancellationToken cancellationToken);
}
