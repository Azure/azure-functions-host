// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    /// <summary>
    /// Class containing registration data used by the <see cref="SharedBlobQueueListener"/>.
    /// </summary>
    internal class BlobQueueRegistration
    {
        /// <summary>
        /// The function executor used to execute the function when a queue
        /// message is received for a blob that needs processing.
        /// </summary>
        public ITriggeredFunctionExecutor Executor { get; set; }

        /// <summary>
        /// The storage client to use to retrieve the blob (i.e., the
        /// storage account that the blob triggered function is listening
        /// to).
        /// </summary>
        public IStorageBlobClient BlobClient { get; set; }
    }
}
