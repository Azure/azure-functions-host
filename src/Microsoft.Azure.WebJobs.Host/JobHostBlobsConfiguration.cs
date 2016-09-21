// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Represents configuration for <see cref="BlobTriggerAttribute"/>.
    /// </summary>
    public class JobHostBlobsConfiguration
    {
        /// <summary>
        /// Constructs a new instance.
        /// </summary>
        public JobHostBlobsConfiguration()
        {
            CentralizedPoisonQueue = false;
        }

        /// <summary>
        /// Gets or sets a value indicating whether a single centralized
        /// poison queue for poison blobs should be used (in the primary
        /// storage account) or whether the poison queue for a blob triggered
        /// function should be co-located with the target blob container.
        /// This comes into play only when using multiple storage accounts via
        /// <see cref="StorageAccountAttribute"/>. The default is false.
        /// </summary>
        public bool CentralizedPoisonQueue { get; set; }
    }
}
