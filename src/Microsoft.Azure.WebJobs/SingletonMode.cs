// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Enumeration of modes that <see cref="SingletonAttribute"/> can
    /// operate in.
    /// </summary>
    public enum SingletonMode
    {
        /// <summary>
        /// Indicates a singleton lock that is taken before each
        /// function invocation, and released immediately after.
        /// This is the default.
        /// </summary>
        Function,

        /// <summary>
        /// Indicates a singleton lock that is taken when starting the
        /// listener for a triggered function. Using this mode, the listener
        /// (and therefore the function) will only be running on a single instance
        /// (when scaled out).
        /// </summary>
        Listener
    }
}
