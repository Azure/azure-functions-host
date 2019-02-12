// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public class SyncTriggersResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the sync operation was successful.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Gets or sets the error string in the case of a failed sync operation.
        /// </summary>
        public string Error { get; set; }
    }
}
