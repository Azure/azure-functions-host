// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.Grpc.HttpProxy;

internal sealed class HttpProxyAdmission(int maximumRequests, long maximumBytes)
{
    private readonly Lock _lock = new();
    private int _requests;
    private long _bytes;

    internal Lease? TryAcquire(long bytes)
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

    internal sealed class Lease : IDisposable
    {
        private Reservation? _reservation;

        private Lease(Reservation reservation)
        {
            _reservation = reservation;
        }

        internal static Lease Create(HttpProxyAdmission owner, long bytes) => new(new Reservation(owner, bytes));

        internal Lease Retain()
        {
            Reservation reservation = _reservation ?? throw new ObjectDisposedException(nameof(Lease));
            reservation.Retain();
            return new Lease(reservation);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _reservation, null) is { } reservation
                && reservation.Release())
            {
                HttpProxyAdmission admission = reservation.Owner;
                lock (admission._lock)
                {
                    admission._requests--;
                    admission._bytes -= reservation.Bytes;
                }
            }
        }
    }

    private sealed class Reservation(HttpProxyAdmission owner, long bytes)
    {
        private int _references = 1;

        internal HttpProxyAdmission Owner { get; } = owner;

        internal long Bytes { get; } = bytes;

        internal void Retain() => Interlocked.Increment(ref _references);

        internal bool Release() => Interlocked.Decrement(ref _references) == 0;
    }
}
