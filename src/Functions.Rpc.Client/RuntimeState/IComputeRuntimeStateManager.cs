// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Tracks the compute Host's total HTTP capacity as versioned snapshots.
/// </summary>
/// <remarks>
/// A new snapshot is created only when capacity changes. The manager observes the worker registry and ScriptHost state;
/// it never changes worker channels.
/// </remarks>
public interface IComputeRuntimeStateManager
{
    /// <summary>
    /// Gets the latest snapshot.
    /// </summary>
    ComputeRuntimeState Current { get; }

    /// <summary>
    /// Waits without polling for a snapshot newer than <paramref name="lastKnownVersion"/>.
    /// </summary>
    /// <param name="lastKnownVersion">The last snapshot version observed by the caller.</param>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>The latest snapshot, whose version is greater than <paramref name="lastKnownVersion"/>.</returns>
    Task<ComputeRuntimeState> WaitForChangeAsync(long lastKnownVersion, CancellationToken cancellationToken = default);
}
