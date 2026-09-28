// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client.Tests;

/// <summary>
/// Simulates the worker channel registry's initialized-channels version and its change wait.
/// </summary>
internal sealed class InitializedChannelsSignal
{
    private long _version;
    private TaskCompletionSource _changed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Gets the current initialized-channels version.
    /// </summary>
    public long Version => Volatile.Read(ref _version);

    /// <summary>
    /// Records an initialized-channels change and wakes the pending waits.
    /// </summary>
    public void Signal()
    {
        Interlocked.Increment(ref _version);
        Interlocked.Exchange(ref _changed, new(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    /// <summary>
    /// Waits until the version is greater than <paramref name="lastKnownVersion"/>.
    /// </summary>
    /// <param name="lastKnownVersion">The version the caller last observed.</param>
    /// <param name="cancellationToken">The token that cancels the wait.</param>
    /// <returns>The version that is greater than <paramref name="lastKnownVersion"/>.</returns>
    public async Task<long> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken)
    {
        while (true)
        {
            // Capture the signal before reading the version so a change between the two reads is not missed.
            Task changed = Volatile.Read(ref _changed).Task;
            long version = Version;
            if (version > lastKnownVersion)
            {
                return version;
            }

            await changed.WaitAsync(cancellationToken);
        }
    }
}
