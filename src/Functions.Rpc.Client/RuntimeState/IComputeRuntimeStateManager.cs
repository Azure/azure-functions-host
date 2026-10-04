// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Tracks linked worker counts and the compute Host's total HTTP capacity as immutable snapshots.
/// </summary>
/// <remarks>
/// A new snapshot is created only when worker counts or capacity change. The manager observes the worker registry and ScriptHost state;
/// it never changes worker channels.
/// </remarks>
public interface IComputeRuntimeStateManager
{
    /// <summary>
    /// Gets the latest snapshot.
    /// </summary>
    ComputeRuntimeState Current { get; }

    /// <summary>
    /// Waits without polling for a snapshot instance different from <paramref name="lastKnownState"/>.
    /// </summary>
    /// <param name="lastKnownState">The last snapshot observed by the caller.</param>
    /// <param name="cancellationToken">A token that cancels only this wait.</param>
    /// <returns>The latest snapshot, which is not the same instance as <paramref name="lastKnownState"/>.</returns>
    /// <remarks>Pass a snapshot obtained from this manager.</remarks>
    Task<ComputeRuntimeState> WaitForChangeAsync(ComputeRuntimeState lastKnownState, CancellationToken cancellationToken = default);
}
