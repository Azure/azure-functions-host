// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;

namespace Azure.Functions.Rpc.Client;

/// <summary>
/// Observes linked worker counts and registry-ready HTTP capacity independently of ScriptHost availability.
/// </summary>
public interface IRuntimeStateManager
{
    /// <summary>
    /// Gets the latest immutable snapshot, retained while its counts and capacity are unchanged.
    /// </summary>
    RuntimeState Current { get; }

    /// <summary>
    /// Waits for a fresh registry observation, including changes that do not alter snapshot values.
    /// </summary>
    /// <param name="cancellationToken">Cancels only this caller's wait.</param>
    /// <returns>The snapshot from a fresh, non-invalidated observation.</returns>
    Task<RuntimeState> RefreshAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets a one-shot task that completes when the current snapshot changes.
    /// </summary>
    /// <returns>A shared task for the next snapshot change.</returns>
    /// <remarks>
    /// Capture the task before reading <see cref="Current"/> to avoid missing a concurrent change.
    /// After completion, capture a new task and refresh <see cref="Current"/>. Cancel only the caller's wait, not the shared task.
    /// </remarks>
    /// <example>
    /// <code>
    /// while (true)
    /// {
    ///     Task changed = manager.GetChangeTask();
    ///     Refresh(manager.Current);
    ///     await changed.WaitAsync(cancellationToken);
    /// }
    /// </code>
    /// </example>
    Task GetChangeTask();
}
