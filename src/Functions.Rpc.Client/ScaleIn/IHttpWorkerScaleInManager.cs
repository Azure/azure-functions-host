// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Provides Host-local HTTP worker scale-in reservations.
/// </summary>
public interface IHttpWorkerScaleInManager
{
    /// <summary>
    /// Reserves the currently eligible HTTP on-demand worker with the lowest ordinal Worker ID.
    /// </summary>
    /// <returns>A reservation and projected capacity snapshot, or <see langword="null"/> when no worker is eligible.</returns>
    HttpWorkerScaleInReservation TryReserveWorker();

    /// <summary>
    /// Converts a reservation into the irreversible worker drain operation.
    /// </summary>
    /// <param name="reservation">The reservation to commit.</param>
    /// <param name="drainCompletion">The shared drain completion task when committed.</param>
    /// <returns><see langword="true"/> when this reservation committed the drain.</returns>
    bool TryCommitReservation(HttpWorkerScaleInReservation reservation, out Task<bool> drainCompletion);

    /// <summary>
    /// Cancels an uncommitted reservation.
    /// </summary>
    /// <param name="reservation">The reservation to cancel.</param>
    /// <param name="invocationAdmissionRestored">
    /// <see langword="true"/> when invocation admission was restored; <see langword="false"/> when Runtime retirement
    /// already owns the admission fence.
    /// </param>
    /// <returns><see langword="true"/> when the reservation was canceled.</returns>
    bool CancelReservation(
        HttpWorkerScaleInReservation reservation,
        out bool invocationAdmissionRestored);
}

/// <summary>
/// Represents one reversible Host-local reservation made before AppServer accepts the capacity reduction.
/// </summary>
public sealed class HttpWorkerScaleInReservation
{
    internal HttpWorkerScaleInReservation(
        string workerId,
        Guid reservationId,
        HostStateSnapshot projectedState)
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
    public HostStateSnapshot ProjectedState { get; }

    internal Guid ReservationId { get; }
}
