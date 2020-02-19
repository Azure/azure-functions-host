﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Workers
{
    public enum FunctionInvocationDispatcherState
    {
        /// <summary>
        /// The FunctionDispatcher has not yet been created
        /// </summary>
        Default,

        /// <summary>
        /// The FunctionDispatcher is starting.
        /// </summary>
        Initializing,

        /// <summary>
        /// The FunctionDispatcher has been fully initialized and can accept direct function
        /// invocations. All functions have been indexed. Listeners may not yet
        /// be not yet running.
        /// </summary>
        Initialized,

        /// <summary>
        /// The FunctionDispatcher was previously "Initialized" but no longer has any initialized
        /// worker processes to handle function invocations.
        /// </summary>
        WorkerProcessRestarting,

        /// <summary>
        /// The FunctionDispatcher is disposing
        /// </summary>
        Disposing
    }
}
