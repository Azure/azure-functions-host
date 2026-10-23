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
/// An initialized channel has completed its transport connection and WorkerInit handshake. A ready channel has also
/// been configured for the active ScriptHost function set and can accept invocations. The registry owns channel setup
/// and disposal; ScriptHost-scoped dispatchers borrow ready channels.
/// </remarks>
public interface IWorkerChannelRegistry : IAsyncDisposable
{
    /// <summary>
    /// Atomically admits one worker or reuses its matching pending or initialized link.
    /// </summary>
    /// <remarks>
    /// This method returns after the outbound transport connects, the worker sends <c>StartStream</c>, the channel
    /// processes a successful <c>WorkerInitResponse</c>, and the channel is registered. Function setup is independent
    /// and is not awaited; it may already have started or completed when the caller observes the result.
    /// Use <see cref="GetReadyChannelsAsync"/> when an invocation-ready channel is required.
    /// Matching worker IDs and gRPC endpoint URIs share initialization. Endpoints are compared using ordinal
    /// <see cref="Uri.AbsoluteUri"/> equality. HTTP proxying is negotiated through worker capabilities.
    /// Different workers can link concurrently.
    /// Conflicting or terminal links are rejected. Failed attempts can retry after cleanup.
    /// A new attempt's deadline is shared with its waiters; a retry's deadline only ends its own wait.
    /// </remarks>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="grpcEndpoint">The absolute FunctionRpc endpoint.</param>
    /// <param name="cancellationToken">Cancels a new link attempt, or only the caller's wait for an existing attempt.</param>
    /// <returns>
    /// The initialized and registered channel, and whether this caller started the successful attempt.
    /// Matching retries report an existing link, including retries that waited for initialization.
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
    /// <returns><see langword="true"/> when the worker is linked; otherwise, <see langword="false"/>.</returns>
    bool TryGetInitializedChannel(string workerId, out WorkerChannel channel);

    /// <summary>
    /// Gets a snapshot of initialized channels.
    /// </summary>
    /// <returns>The initialized channel snapshot.</returns>
    IReadOnlyList<WorkerChannel> GetInitializedChannels();

    /// <summary>
    /// Gets a one-shot task invalidated by the next initialized membership, readiness, or setup-context change.
    /// </summary>
    /// <returns>A shared task that completes on a change or disposal. Obtain it before sampling registry inputs.</returns>
    /// <remarks>Cancel only the caller's wait, not this shared task. Getting a task after disposal throws.</remarks>
    Task GetChangeTask();

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
    /// Waits without polling for the first initialized channel.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>An initialized channel.</returns>
    Task<WorkerChannel> WaitForFirstInitializedAsync(CancellationToken cancellationToken = default);
}
