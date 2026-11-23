// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    /// <summary>
    /// The platform's report for a triggers change notification: whether it stored the triggers payload.
    /// </summary>
    public enum PlatformTriggersStoreStatus
    {
        /// <summary>The platform accepted the notification but didn't confirm the store (confirmation not enabled).</summary>
        NotConfirmed,

        /// <summary>The platform stored the payload.</summary>
        Stored,

        /// <summary>The platform didn't need to store the payload (for example, the worker isn't on the live package).</summary>
        Skipped,

        /// <summary>The platform failed to pull or store the payload.</summary>
        Failed,

        /// <summary>The platform didn't report the outcome in time.</summary>
        Timeout,
    }
}
