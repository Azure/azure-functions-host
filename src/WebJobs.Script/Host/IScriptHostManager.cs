// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IScriptHostManager
    {
        /// <summary>
        /// Host Initializing event delegate; called during Script Host initialization.
        /// </summary>
        event EventHandler HostInitializing;

        /// <summary>
        /// Event raised when the active host managed by this instance changes.
        /// </summary>
        event EventHandler<ActiveHostChangedEventArgs> ActiveHostChanged;

        /// <summary>
        /// Gets the current state of the script host.
        /// </summary>
        ScriptHostState State { get; }

        /// <summary>
        /// Gets a version that increases whenever <see cref="State"/> or the active host changes.
        /// </summary>
        /// <remarks>Read this before sampling state or services, then wait on that version to avoid missed changes.</remarks>
        long StateVersion { get; }

        /// <summary>
        /// Gets the last host <see cref="Exception"/> that has occurred.
        /// </summary>
        Exception? LastError { get; }

        /// <summary>
        /// Gets the current <see cref="IServiceProvider"/> for the active Script Host.
        /// </summary>
        IServiceProvider? Services { get; }

        /// <summary>
        /// Restarts the current Script Job Host.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when the host is restarted.</returns>
        Task RestartHostAsync(string reason, CancellationToken cancellationToken = default);

        /// <summary>
        /// Waits without polling until <see cref="StateVersion"/> exceeds <paramref name="lastKnownVersion"/>.
        /// </summary>
        /// <param name="lastKnownVersion">The last version observed by the caller.</param>
        /// <param name="cancellationToken">The token that cancels the wait.</param>
        /// <returns>The current version, greater than <paramref name="lastKnownVersion"/>.</returns>
        /// <remarks>
        /// Changes remain observable even when the state returns to its previous value before the waiter resumes.
        /// Canceling a wait does not affect other waiters.
        /// </remarks>
        /// <example>
        /// <code>
        /// long version = manager.StateVersion;
        /// while (manager.State != ScriptHostState.Running)
        /// {
        ///     version = await manager.WaitForStateChangeAsync(version, cancellationToken);
        /// }
        /// </code>
        /// </example>
        Task<long> WaitForStateChangeAsync(long lastKnownVersion, CancellationToken cancellationToken = default);
    }
}