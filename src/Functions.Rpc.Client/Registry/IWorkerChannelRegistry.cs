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
    /// Matching worker IDs, credentials and gRPC endpoint URIs share initialization. Endpoints are compared using ordinal
    /// <see cref="Uri.AbsoluteUri"/> equality. HTTP proxying is negotiated through WorkerProxy capabilities.
    /// Different workers can link concurrently.
    /// Conflicting or terminal links are rejected. Failed attempts can retry after cleanup.
    /// A new attempt's deadline is shared with its waiters; a retry's deadline only ends its own wait.
    /// </remarks>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="grpcEndpoint">The absolute FunctionRpc endpoint.</param>
    /// <param name="runtimeAccessKey">The target WorkerProxy's container encryption key. Never log this value.</param>
    /// <param name="cancellationToken">Cancels a new link attempt, or only the caller's wait for an existing attempt.</param>
    /// <returns>
    /// The handshake-complete, registered channel, and whether this caller started the successful attempt.
    /// Matching retries report an existing link, including retries that waited for its handshake.
    /// </returns>
    /// <exception cref="WorkerLinkException">The runtime access key is invalid or the request conflicts with the registry's admission state.</exception>
    /// <exception cref="TimeoutException">The new link attempt or this caller's retry wait exceeded its deadline.</exception>
    Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        string runtimeAccessKey,
        CancellationToken cancellationToken = default)
        => LinkAsync(workerId, grpcEndpoint, runtimeAccessKey, runtimeIdentity: null, cancellationToken);

    /// <summary>
    /// Links a Worker with caller authentication while conveying the platform's serialized Runtime identity.
    /// </summary>
    /// <param name="workerId">The worker identifier.</param>
    /// <param name="grpcEndpoint">The absolute FunctionRpc endpoint.</param>
    /// <param name="runtimeAccessKey">The target WorkerProxy's container encryption key. Never log this value.</param>
    /// <param name="runtimeIdentity">Optional serialized Runtime identity, included in exact retry matching.</param>
    /// <param name="cancellationToken">Cancels the link attempt or retry wait.</param>
    /// <returns>The initialized and registered channel and whether the link is new.</returns>
    Task<WorkerLinkResult> LinkAsync(
        string workerId,
        Uri grpcEndpoint,
        string runtimeAccessKey,
        string runtimeIdentity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically commits excess usable workers in one exact non-HTTP function group and capacity class to drain.
    /// </summary>
    /// <param name="functionGroup">The exact trusted function group.</param>
    /// <param name="isAlwaysReady">The exact always-ready capacity class.</param>
    /// <param name="desiredWorkerCount">The absolute number of matching usable workers to retain.</param>
    /// <returns>The number of workers newly committed to drain.</returns>
    int ScaleInWorkers(
        string functionGroup,
        bool isAlwaysReady,
        int desiredWorkerCount);

    /// <summary>
    /// Selects excess workers, publishing reduced HTTP Always Ready capacity before committing their drain.
    /// </summary>
    /// <param name="functionGroup">The trusted function group; non-HTTP groups use exact matching.</param>
    /// <param name="isAlwaysReady">The exact capacity class. HTTP on-demand scale-in is unsupported.</param>
    /// <param name="desiredWorkerCount">The absolute number of matching usable workers to retain.</param>
    /// <param name="publishCapacity">Publishes freshly computed capacity and completes only after a successful outbound response.</param>
    /// <param name="cancellationToken">Cancels selection or publication before drain commitment.</param>
    /// <returns>The number of newly draining workers, without waiting for accepted work to finish.</returns>
    /// <remarks>HTTP selections are serialized. Failed publication releases reserved capacity without starting drain.</remarks>
    Task<int> ScaleInWorkersAsync(
        string functionGroup,
        bool isAlwaysReady,
        int desiredWorkerCount,
        Func<CancellationToken, Task> publishCapacity,
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
    /// Gets a read-only snapshot of initialized channels without waiting for function setup.
    /// </summary>
    /// <returns>The initialized channel snapshot.</returns>
    IReadOnlyList<WorkerChannel> GetInitializedChannels();

    /// <summary>
    /// Gets linked channels, including workers still preparing their extension source.
    /// </summary>
    /// <returns>A read-only membership snapshot for linked-count publication, not invocation readiness.</returns>
    IReadOnlyList<WorkerChannel> GetLinkedChannels() => GetInitializedChannels();

    /// <summary>
    /// Gets a one-shot task invalidated by the next initialized membership, readiness, or setup-context change.
    /// </summary>
    /// <returns>A shared task that completes on a change or disposal. Obtain it before sampling registry inputs.</returns>
    /// <remarks>
    /// Reading the task does not wait for function setup. Cancel only the caller's wait, not this shared task.
    /// Getting a task after disposal throws.
    /// </remarks>
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
    Task ActivateAsync(
        IReadOnlyList<FunctionMetadata> functions,
        ManagedDependencyOptions managedDependencyOptions,
        TimeSpan? functionTimeout,
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
