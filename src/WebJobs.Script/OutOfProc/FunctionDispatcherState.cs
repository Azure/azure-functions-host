// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Rpc
{
    public enum FunctionDispatcherState
    {
        /// <summary>
        /// The FunctionDispatcher has not yet been created
        /// </summary>
        Default,

        /// <summary>
        /// The FunctionDispatcherState is starting.
        /// </summary>
        Initializing,

        /// <summary>
        /// The FunctionDispatcherState has been fully initialized and can accept direct function
        /// invocations. All functions have been indexed. Listeners may not yet
        /// be not yet running.
        /// </summary>
        Initialized
    }
}
