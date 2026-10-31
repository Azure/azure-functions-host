// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Coordinates the authoritative Host capacity snapshot and generation.
/// </summary>
public interface IHostStateCoordinator
{
    /// <summary>
    /// Gets the current linked-worker and usable HTTP-capacity snapshot.
    /// </summary>
    HostStateSnapshot Current { get; }

    /// <summary>
    /// Gets the current Host-state generation.
    /// </summary>
    long Version { get; }

    /// <summary>
    /// Waits until the Host-state generation changes from the supplied version.
    /// </summary>
    /// <param name="version">The last observed Host-state generation.</param>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>The newer Host-state generation.</returns>
    Task<long> WaitForChangeAsync(
        long version,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents one immutable Host capacity snapshot.
/// </summary>
/// <param name="CreatedTime">The UTC creation time used by the AppServer publication contract.</param>
/// <param name="HttpCapacity">The total usable HTTP request-slot capacity.</param>
/// <param name="LinkedWorkerCount">
/// The number of physical Worker links, including links detached from admission while disposal is still running.
/// </param>
/// <param name="Version">The Host-local generation used to validate state transitions.</param>
public sealed record HostStateSnapshot(
    DateTimeOffset CreatedTime,
    long HttpCapacity,
    int LinkedWorkerCount,
    long Version);

internal sealed class HostStateClock
{
    private readonly TimeProvider _timeProvider;

    public HostStateClock(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        Current = new(_timeProvider.GetUtcNow(), 0, 0, 0);
    }

    public HostStateSnapshot Current { get; private set; }

    public HostStateSnapshot Advance(long httpCapacity, int linkedWorkerCount)
    {
        DateTimeOffset createdTime = _timeProvider.GetUtcNow();
        if (createdTime <= Current.CreatedTime)
        {
            createdTime = Current.CreatedTime.AddTicks(1);
        }

        Current = new(createdTime, httpCapacity, linkedWorkerCount, Current.Version + 1);
        return Current;
    }
}
