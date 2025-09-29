// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

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
        /// Gets the services from the current script host.
        /// </summary>
        IServiceProvider Services { get; }

        /// <summary>
        /// Gets the last host <see cref="Exception"/> that has occurred.
        /// </summary>
        Exception LastError { get; }

        /// <summary>
        /// Restarts the current Script Job Host.
        /// </summary>
        /// <returns>A <see cref="Task"/> that completes when the host is restarted.</returns>
        Task RestartHostAsync(string reason, CancellationToken cancellationToken = default);
    }
}