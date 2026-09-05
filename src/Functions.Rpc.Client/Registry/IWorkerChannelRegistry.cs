// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Grpc;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Defines initialized client-backed worker channel ownership keyed by worker ID.
/// </summary>
/// <remarks>
/// An initialized channel has completed its transport connection and WorkerInit handshake. Invocation readiness remains
/// a dispatcher concern. The registry owns disposal of returned channels.
/// </remarks>
internal interface IWorkerChannelRegistry : IAsyncDisposable
{
    /// <summary>
    /// Atomically admits one worker or reuses its matching pending or initialized link.
    /// </summary>
    /// <remarks>
    /// This method returns after the outbound transport connects, the worker sends <c>StartStream</c>, and the channel
    /// processes a successful <c>WorkerInitResponse</c>. Function metadata has not been requested, invocation buffers
    /// have not been created, and function load requests have not been sent, so the channel is not yet ready for
    /// invocations.
    /// Matching worker IDs and endpoint URIs share initialization. Different workers can link concurrently.
    /// Conflicting or terminal links are rejected. Failed attempts can retry after cleanup.
    /// </remarks>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="grpcEndpoint">The absolute FunctionRpc endpoint.</param>
    /// <param name="cancellationToken">Cancels a new link attempt, or only the caller's wait for an existing attempt.</param>
    /// <returns>The channel after its FunctionRpc initialization handshake completes.</returns>
    /// <exception cref="WorkerLinkException">The request conflicts with the registry's admission state.</exception>
    Task<WorkerChannel> LinkAsync(string workerId, Uri grpcEndpoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes and disposes a linked worker when present.
    /// </summary>
    /// <remarks>The accepted worker ID remains terminal and cannot be linked again in this registry.</remarks>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="cancellationToken">A token that cancels waiting to begin the unlink.</param>
    /// <returns><see langword="true"/> when a channel was removed; otherwise, <see langword="false"/>.</returns>
    Task<bool> UnlinkAsync(string workerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to get an initialized channel.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="channel">The initialized channel when found.</param>
    /// <returns><see langword="true"/> when the worker is linked; otherwise, <see langword="false"/>.</returns>
    bool TryGetInitializedChannel(string workerId, out WorkerChannel channel);

    /// <summary>
    /// Gets a snapshot of initialized channels.
    /// </summary>
    /// <returns>The initialized channel snapshot.</returns>
    IReadOnlyList<WorkerChannel> GetInitializedChannels();

    /// <summary>
    /// Waits without polling for the first initialized channel.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>An initialized channel.</returns>
    Task<WorkerChannel> WaitForFirstInitializedAsync(CancellationToken cancellationToken = default);
}
