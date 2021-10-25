// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Models
{
    public enum DrainModeState
    {
        /// <summary>
        /// Drain mode is disabled
        /// </summary>
        Disabled,

        /// <summary>
        /// Drain mode is enabled and there are active invocations or retries
        /// </summary>
        InProgress,

        /// <summary>
        /// Drain mode is enabled and there are no active invocations or retries
        /// </summary>
        Completed
    }
}
