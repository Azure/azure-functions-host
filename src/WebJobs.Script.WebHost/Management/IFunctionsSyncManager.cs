// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Management
{
    public interface IFunctionsSyncManager
    {
        /// <summary>
        /// Sync function triggers with Antares infrastructure.
        /// </summary>
        /// <param name="isBackgroundSync">Indicates whether this is a background sync operation.</param>
        /// <returns>The <see cref="SyncTriggersResult"/> for the request.</returns>
        Task<SyncTriggersResult> TrySyncTriggersAsync(bool isBackgroundSync = false);
    }
}
