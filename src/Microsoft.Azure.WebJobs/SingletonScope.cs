// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs
{
    /// <summary>
    /// Enumeration of values used to control the scope of Singleton locks.
    /// </summary>
    public enum SingletonScope
    {
        /// <summary>
        /// Indicates a Singleton lock that is scoped a single function.
        /// </summary>
        Function,

        /// <summary>
        /// Indicates a Singleton lock that is scoped to the JobHost.
        /// <remarks>
        /// This is useful in scenarios where locking across multiple
        /// functions is desired. In this case the same attribute can
        /// be applied to multiple functions, and they'll share the
        /// same lock.
        /// </remarks>
        /// </summary>
        Host
    }
}
