// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Defines root-owned client-backed worker channel ownership keyed by worker ID.
/// </summary>
/// <remarks>
/// An initialized channel has completed its transport connection, WorkerInit handshake, and extension-source preparation.
/// Advertised extension assemblies have been received and extracted to disk, or the existing extension source has been selected.
/// A ready channel has also been configured for the active ScriptHost function set and can accept invocations.
/// The registry owns channel setup and disposal; ScriptHost-scoped dispatchers borrow ready channels.
/// </remarks>
public interface IWorkerChannelRegistry : IAsyncDisposable
{
    /// <summary>
    /// Atomically admits one worker or reuses its matching pending or registered link.
    /// </summary>
    /// <remarks>
    /// This method returns after the outbound transport connects, the worker sends <c>StartStream</c>, the channel
    /// processes a successful <c>WorkerInitResponse</c>, and the channel is registered. Extension-source preparation
    /// and function setup are independent and are not awaited. Use <see cref="WaitForFirstInitializedAsync"/> before
    /// metadata acquisition or ScriptHost startup so extension assemblies are available on disk.
    /// Use <see cref="GetReadyChannelsAsync"/> when an invocation-ready channel is required.
    /// Matching worker IDs and gRPC endpoint URIs share the link handshake. Endpoints are compared using ordinal
    /// <see cref="Uri.AbsoluteUri"/> equality. HTTP proxying is negotiated through worker capabilities.
    /// Different workers can link concurrently.
    /// Conflicting or terminal links are rejected. Failed attempts can retry after cleanup.
    /// A new attempt's deadline is shared with its waiters; a retry's deadline only ends its own wait.
    /// </remarks>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="grpcEndpoint">The absolute FunctionRpc endpoint.</param>
    /// <param name="cancellationToken">Cancels a new link attempt, or only the caller's wait for an existing attempt.</param>
    /// <returns>
    /// The handshake-complete, registered channel, and whether this caller started the successful attempt.
    /// Matching retries report an existing link, including retries that waited for its handshake.
    /// </returns>
    /// <exception cref="WorkerLinkException">The request conflicts with the registry's admission state.</exception>
    /// <exception cref="TimeoutException">The new link attempt or this caller's retry wait exceeded its deadline.</exception>
    Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        CancellationToken cancellationToken = default);

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
    /// <returns><see langword="true"/> when the worker has completed initialization; otherwise, <see langword="false"/>.</returns>
    bool TryGetInitializedChannel(string workerId, out WorkerChannel channel);

    /// <summary>
    /// Gets a snapshot of initialized channels.
    /// </summary>
    /// <returns>The initialized channel snapshot.</returns>
    IReadOnlyList<WorkerChannel> GetInitializedChannels();

    /// <summary>
    /// Activates a new ScriptHost setup context and configures every currently initialized channel for it.
    /// </summary>
    /// <param name="functions">The final valid functions for the ScriptHost.</param>
    /// <param name="managedDependencyOptions">The managed dependency options used for function load requests.</param>
    /// <param name="functionTimeout">The configured function timeout.</param>
    /// <param name="cancellationToken">A token that cancels activation before setup begins.</param>
    /// <returns>A task that completes when all currently initialized channels have been attempted.</returns>
    /// <remarks>
    /// Empty function lists are valid and replace the prior setup context. Activation succeeds when there are no
    /// channels or at least one channel is configured successfully. If every current channel fails, the first original
    /// setup exception is rethrown.
    /// </remarks>
    Task ActivateAsync(IReadOnlyList<FunctionMetadata> functions, ManagedDependencyOptions managedDependencyOptions, TimeSpan? functionTimeout,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current ready-channel snapshot or waits for the first channel ready for the active setup context.
    /// </summary>
    /// <returns>
    /// A value task completed synchronously with a current non-empty snapshot when one exists; otherwise, a shared task
    /// that completes with the first non-empty ready snapshot.
    /// </returns>
    /// <remarks>
    /// Reading readiness never waits for channel setup or takes the registry's setup lock. A pending wait follows the
    /// active ScriptHost context across activation and fails if the registry is disposed.
    /// </remarks>
    ValueTask<IReadOnlyList<WorkerChannel>> GetReadyChannelsAsync();

    /// <summary>
    /// Waits without polling for a live channel whose handshake and extension-source preparation are complete.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this wait, not a linked worker's preparation.</param>
    /// <returns>An initialized channel that can supply metadata for the selected extension source.</returns>
    /// <remarks>
    /// Advertised extension assemblies are received and extracted to disk before this wait completes. Workers without
    /// payload support retain the existing extension-source behavior. This gate precedes ScriptHost construction
    /// but does not mean extension startups have run or invocation listeners are ready. Link completion is independent.
    /// </remarks>
    Task<WorkerChannel> WaitForFirstInitializedAsync(CancellationToken cancellationToken = default);
}
