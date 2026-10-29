// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Grpc.TokenServiceProxy;

/// <summary>
/// Reserves request slots and conservative buffer bytes atomically, without introducing a capacity wait queue.
/// </summary>
/// <param name="maximumRequests">The maximum number of concurrent reservations.</param>
/// <param name="maximumBytes">The maximum total buffer capacity reserved in bytes.</param>
public sealed class TokenServiceProxyAdmission(int maximumRequests, long maximumBytes)
{
    private readonly Lock _lock = new();
    private int _requests;
    private long _bytes;

    /// <summary>
    /// Reserves one request and its buffer allowance immediately, or returns null when either limit is exhausted.
    /// </summary>
    /// <param name="bytes">The buffer capacity to reserve for this request in bytes.</param>
    /// <returns>A lease that releases capacity when disposed, or <see langword="null"/> when capacity is unavailable.</returns>
    public Lease? TryAcquire(long bytes)
    {
        lock (_lock)
        {
            if (_requests >= maximumRequests || bytes > maximumBytes - _bytes)
            {
                return null;
            }

            _requests++;
            _bytes += bytes;
            return Lease.Create(this, bytes);
        }
    }

    /// <summary>
    /// Shares one reservation between callers and transport cleanup; only the last owner releases capacity.
    /// </summary>
    public sealed class Lease : IDisposable
    {
        private Reservation? _reservation;

        private Lease(Reservation reservation)
        {
            _reservation = reservation;
        }

        internal static Lease Create(TokenServiceProxyAdmission owner, long bytes) => new(new Reservation(owner, bytes));

        /// <summary>
        /// Keeps the reservation alive when queued or in-flight transport work outlives its HTTP caller.
        /// </summary>
        internal Lease Retain()
        {
            Reservation reservation = _reservation ?? throw new ObjectDisposedException(nameof(Lease));
            reservation.Retain();
            return new Lease(reservation);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _reservation, null) is { } reservation
                && reservation.Release())
            {
                TokenServiceProxyAdmission admission = reservation.Owner;
                lock (admission._lock)
                {
                    admission._requests--;
                    admission._bytes -= reservation.Bytes;
                }
            }
        }
    }

    private sealed class Reservation(TokenServiceProxyAdmission owner, long bytes)
    {
        private int _references = 1;

        internal TokenServiceProxyAdmission Owner { get; } = owner;

        internal long Bytes { get; } = bytes;

        internal void Retain() => Interlocked.Increment(ref _references);

        internal bool Release() => Interlocked.Decrement(ref _references) == 0;
    }
}
