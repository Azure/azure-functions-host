// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides Host-local HTTP worker capacity and idle scale-in reservations.
/// </summary>
public interface IHttpWorkerScaleInManager
{
    /// <summary>
    /// Gets the current linked-worker and usable HTTP-capacity snapshot.
    /// </summary>
    HttpWorkerScaleInState Current { get; }

    /// <summary>
    /// Gets the current state version.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Waits until the scale-in state changes from the supplied version.
    /// </summary>
    /// <param name="version">The last observed state version.</param>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>The newer state version.</returns>
    Task<long> WaitForChangeAsync(long version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reserves one invocation-ready HTTP on-demand worker that has remained idle for the supplied duration.
    /// </summary>
    /// <param name="minimumIdleDuration">The required continuous idle duration.</param>
    /// <returns>A reservation and projected capacity snapshot, or <see langword="null"/> when no worker is eligible.</returns>
    HttpWorkerScaleInReservation TryReserveIdleWorker(TimeSpan minimumIdleDuration);

    /// <summary>
    /// Converts a reservation into the irreversible worker drain operation.
    /// </summary>
    /// <param name="reservation">The reservation to commit.</param>
    /// <param name="drainCompletion">The shared drain completion task when committed.</param>
    /// <returns><see langword="true"/> when this reservation committed the drain.</returns>
    bool TryCommitReservation(HttpWorkerScaleInReservation reservation, out Task<bool> drainCompletion);

    /// <summary>
    /// Cancels an uncommitted reservation and restores invocation admission.
    /// </summary>
    /// <param name="reservation">The reservation to cancel.</param>
    /// <returns><see langword="true"/> when the reservation was canceled.</returns>
    bool CancelReservation(HttpWorkerScaleInReservation reservation);
}

/// <summary>
/// Represents one immutable Host capacity snapshot.
/// </summary>
/// <param name="CreatedTime">The UTC creation time used by the AppServer publication contract.</param>
/// <param name="HttpCapacity">The total usable HTTP request-slot capacity.</param>
/// <param name="LinkedWorkerCount">The number of initialized linked workers.</param>
/// <param name="Version">The Host-local generation used to validate reservation commit.</param>
public sealed record HttpWorkerScaleInState(
    DateTimeOffset CreatedTime,
    long HttpCapacity,
    int LinkedWorkerCount,
    long Version);

/// <summary>
/// Represents one reversible Host-local reservation made before AppServer accepts the capacity reduction.
/// </summary>
public sealed class HttpWorkerScaleInReservation
{
    internal HttpWorkerScaleInReservation(
        string workerId,
        Guid reservationId,
        HttpWorkerScaleInState projectedState)
    {
        WorkerId = workerId;
        ReservationId = reservationId;
        ProjectedState = projectedState;
    }

    /// <summary>
    /// Gets the reserved worker identifier.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Gets the capacity snapshot projected after removing the reserved worker from usable capacity.
    /// </summary>
    public HttpWorkerScaleInState ProjectedState { get; }

    internal Guid ReservationId { get; }
}
