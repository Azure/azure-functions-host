// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    /// <summary>
    /// Provides a mechanism for components in the inner (script) host to observe and trigger
    /// lifecycle events of the outer (web) host application.
    /// </summary>
    public interface IScriptApplicationLifetime
    {
        /// <summary>
        /// Triggered when the application host has fully started.
        /// </summary>
        CancellationToken ApplicationStarted { get; }

        /// <summary>
        /// Triggered when the application host is performing a graceful shutdown. Shutdown will block until all callbacks registered on this token have completed.
        /// </summary>
        CancellationToken ApplicationStopping { get; }

        /// <summary>
        /// Triggered when the application host has completed a graceful shutdown. The application will not exit until all callbacks registered on this token have completed.
        /// </summary>
        CancellationToken ApplicationStopped { get; }

        /// <summary>
        /// Requests termination of the current application.
        /// </summary>
        void StopApplication();
    }
}
