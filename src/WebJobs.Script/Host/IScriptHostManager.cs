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
        /// Waits until <see cref="State"/> differs from <paramref name="lastKnownState"/>, without polling.
        /// </summary>
        /// <param name="lastKnownState">The state the caller last observed.</param>
        /// <param name="cancellationToken">The token that cancels the wait.</param>
        /// <returns>The current state, which differs from <paramref name="lastKnownState"/>.</returns>
        /// <remarks>
        /// The wait completes immediately if the state already differs. A state that changes and then returns to
        /// <paramref name="lastKnownState"/> before the waiter observes it does not complete the wait.
        /// </remarks>
        /// <example>
        /// <code>
        /// ScriptHostState state = manager.State;
        /// while (state != ScriptHostState.Running)
        /// {
        ///     state = await manager.WaitForStateChangeAsync(state, cancellationToken);
        /// }
        /// </code>
        /// </example>
        Task<ScriptHostState> WaitForStateChangeAsync(ScriptHostState lastKnownState, CancellationToken cancellationToken = default);
    }
}