// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Links one platform-managed worker to a compute runtime.
/// </summary>
public interface IWorkerLinker
{
    /// <summary>
    /// Connects and registers a worker after StartStream and successful WorkerInitResponse processing.
    /// </summary>
    /// <param name="workerId">The platform identifier, compared using ordinal equality.</param>
    /// <param name="grpcEndpoint">The absolute HTTP or HTTPS FunctionRpc service authority.</param>
    /// <param name="cancellationToken">
    /// Cancels a new link and its resources, or only this caller's wait when replaying a pending link.
    /// </param>
    /// <returns>A task that completes after initialization and registry publication; no channel ownership is transferred.</returns>
    /// <remarks>
    /// A matching ID and endpoint share one attempt. Different workers can link concurrently.
    /// Conflicting or terminal links are rejected.
    /// Failed attempts are removed before a retry can start. Initialization has a thirty-second deadline.
    /// Completion does not imply invocation readiness or ScriptHost startup.
    /// </remarks>
    /// <exception cref="WorkerLinkException">Admission was rejected or initialization was unavailable.</exception>
    Task LinkAsync(string workerId, Uri grpcEndpoint, CancellationToken cancellationToken = default);
}
