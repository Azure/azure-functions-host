// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Azure.Functions.Rpc.Client;

namespace Azure.Functions.Host.HostState;

/// <summary>
/// Sends Host worker-count snapshots to the co-located AppServer.
/// </summary>
internal interface IAppServerHostStateClient
{
    /// <summary>
    /// Sends one snapshot.
    /// </summary>
    /// <param name="state">The snapshot to send.</param>
    /// <param name="cancellationToken">A token that cancels the request.</param>
    /// <returns>The AppServer response status code.</returns>
    Task<HttpStatusCode> PublishAsync(ComputeRuntimeState state, CancellationToken cancellationToken);
}
