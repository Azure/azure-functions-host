// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host.Bindings
{
    /// <summary>
    /// An enumeration of binding order values.
    /// </summary>
    public enum BindStepOrder
    {
        /// <summary>
        /// Default bind order
        /// </summary>
        Default = 0,

        /// <summary>
        /// Enqueue bind order
        /// </summary>
        Enqueue = 1
    }
}
