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

        ScriptHostState State { get; }

        /// <summary>
        /// Gets the last host <see cref="Exception"/> that has occurred.
        /// </summary>
        Exception LastError { get; }

        /// <summary>
        /// Restarts the current Script Job Host
        /// </summary>
        /// <returns>A <see cref="Task"/> that will completed when the host is restarted.</returns>
        Task RestartHostAsync(CancellationToken cancellationToken = default);
    }
}