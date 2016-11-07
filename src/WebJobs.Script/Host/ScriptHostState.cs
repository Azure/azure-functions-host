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
        /// The host has been created and can accept direct function
        /// invocations, but listeners are not yet started.
        /// </summary>
        Created,
        /// <summary>
        /// The host is fully running.
        /// </summary>
        Running,
        /// <summary>
        /// The host is in an error state
        /// </summary>
        Error
    }
}
