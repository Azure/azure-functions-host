// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.Workers;

/// <summary>
/// Tracks runtime-wide state relevant to the App Server: the set of linked
/// workers and the pool of request slots they contribute. State changes are
/// signalled via <see cref="StateChanged"/> so a publisher can forward
/// snapshots to the mesh service.
/// </summary>
/// <remarks>
/// Only registered when compute separation is active
/// (<c>FUNCTIONS_WORKER_EXTERNAL_ENABLED=true</c>). Callers outside the
/// compute-separation code path should not take a dependency on this type.
/// </remarks>
public interface IRuntimeStateManager
{
    /// <summary>
    /// Raised on every state-producing mutation (worker link/unlink,
    /// capacity change, slot acquire/release). Handlers should be cheap and
    /// non-blocking - the event is raised from the mutating thread.
    /// </summary>
    event Action StateChanged;

    /// <summary>
    /// Returns an immutable snapshot of the current runtime state.
    /// Safe to call from any thread.
    /// </summary>
    RuntimeState GetState();

    /// <summary>
    /// Records a newly-linked worker. Counted in
    /// <see cref="RuntimeState.LinkedWorkerCount"/> for its entire tracking
    /// lifetime, regardless of health. Idempotent on duplicate id.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker id.</param>
    void OnWorkerLinked(string workerId);

    /// <summary>
    /// Removes a previously-linked worker from tracking. No-op if the worker
    /// was never linked. Does not alter capacity - callers should call
    /// <see cref="OnWorkerCapacityUnavailable"/> first if capacity is still
    /// being advertised for this worker.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker id.</param>
    void OnWorkerUnlinked(string workerId);

    /// <summary>
    /// Declares that a linked worker is now healthy and contributing the
    /// specified slot capacity to the shared pool. Called after the init
    /// handshake succeeds. Idempotent: if capacity is already tracked for
    /// the worker the call is ignored.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker id.</param>
    /// <param name="slotCapacity">
    /// The worker's max invocation concurrency. Must be positive.
    /// </param>
    void OnWorkerCapacityAvailable(string workerId, int slotCapacity);

    /// <summary>
    /// Declares that a linked worker can no longer serve new invocations
    /// (e.g. drain has begun). Subtracts the capacity that was added for it.
    /// No-op if the worker never contributed capacity. The worker remains
    /// linked until <see cref="OnWorkerUnlinked"/> is called.
    /// </summary>
    /// <param name="workerId">Platform-assigned worker id.</param>
    void OnWorkerCapacityUnavailable(string workerId);

    /// <summary>
    /// Reserves up to <paramref name="requestedSlotCount"/> request slots,
    /// granting a partial amount if fewer are available.
    /// </summary>
    /// <param name="requestedSlotCount">
    /// Number of slots the caller would like to acquire. Must be positive.
    /// </param>
    /// <returns>
    /// The number of slots actually reserved. Zero when no slots are available;
    /// less than <paramref name="requestedSlotCount"/> when only a partial grant
    /// was possible; equal to <paramref name="requestedSlotCount"/> on a full grant.
    /// </returns>
    int AcquireSlots(int requestedSlotCount);

    /// <summary>
    /// Waits until at least one request slot is available, then reserves up to
    /// <paramref name="requestedSlotCount"/> slots atomically.
    /// </summary>
    /// <param name="requestedSlotCount">
    /// Number of slots the caller would like to acquire. Must be positive.
    /// </param>
    /// <param name="timeout">Maximum amount of time to wait for capacity.</param>
    /// <param name="cancellationToken">Cancellation token for the wait.</param>
    /// <returns>
    /// The number of slots reserved, or zero if no capacity became available
    /// before timeout or the runtime stopped.
    /// </returns>
    Task<int> AcquireSlotsAsync(
        int requestedSlotCount,
        TimeSpan timeout,
        CancellationToken cancellationToken);

    /// <summary>
    /// Releases previously-acquired request slots. The leased count is clamped
    /// at zero so over-releases cannot produce negative values.
    /// </summary>
    /// <param name="count">Number of slots to release. Must be positive.</param>
    void ReleaseSlots(int count);

    /// <summary>
    /// Marks the runtime as stopping. From this point on <see cref="GetState"/>
    /// reports zero for <see cref="RuntimeState.TotalRequestSlots"/> and
    /// <see cref="RuntimeState.TotalAvailableRequestSlots"/>, and
    /// <see cref="AcquireSlots"/> grants nothing. Intended to be called once,
    /// by the worker connection manager, at the start of host shutdown. The
    /// transition is a one-way latch; subsequent calls are no-ops.
    /// </summary>
    void SetStopping();
}
