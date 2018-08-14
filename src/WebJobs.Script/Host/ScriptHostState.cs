// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script
{
    public enum ScriptHostState
    {
        /// <summary>
        /// The host has not yet been created
        /// </summary>
        Default,

        /// <summary>
        /// The host is starting.
        /// </summary>
        Starting,

        /// <summary>
        /// The host has been fully initialized and can accept direct function
        /// invocations. All functions have been indexed. Listeners may not yet
        /// be not yet running.
        /// </summary>
        Initialized,

        /// <summary>
        /// The host is fully running.
        /// </summary>
        Running,

        /// <summary>
        /// The host is in an error state
        /// </summary>
        Error,

        /// <summary>
        /// The host is stopping.
        /// </summary>
        Stopping,

        /// <summary>
        /// The host is stopped.
        /// </summary>
        Stopped,

        /// <summary>
        /// The host is offline
        /// </summary>
        Offline
    }
}
