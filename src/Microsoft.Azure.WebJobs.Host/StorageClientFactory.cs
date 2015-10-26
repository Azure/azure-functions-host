// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host
{
    /// <summary>
    /// Factory class for the creation of all Azure Storage clients used by by a <see cref="JobHost"/>.
    /// </summary>
    /// <remarks>
    /// Subclasses can override the various methods to customize client creation.
    /// See <see cref="JobHostConfiguration.StorageClientFactory"/>.
    /// </remarks>
    [CLSCompliant(false)]
    public class StorageClientFactory
    {
        /// <summary>
        /// Creates a <see cref="CloudBlobClient"/> instance for the specified <see cref="StorageClientFactoryContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="StorageClientFactoryContext"/>.</param>
        /// <returns>The <see cref="CloudBlobClient"/>.</returns>
        public virtual CloudBlobClient CreateCloudBlobClient(StorageClientFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            return context.Account.CreateCloudBlobClient();
        }

        /// <summary>
        /// Creates a <see cref="CloudTableClient"/> instance for the specified <see cref="StorageClientFactoryContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="StorageClientFactoryContext"/>.</param>
        /// <returns>The <see cref="CloudTableClient"/>.</returns>
        public virtual CloudTableClient CreateCloudTableClient(StorageClientFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            return context.Account.CreateCloudTableClient();
        }

        /// <summary>
        /// Creates a <see cref="CloudQueueClient"/> instance for the specified <see cref="StorageClientFactoryContext"/>.
        /// </summary>
        /// <param name="context">The <see cref="StorageClientFactoryContext"/>.</param>
        /// <returns>The <see cref="CloudQueueClient"/>.</returns>
        public virtual CloudQueueClient CreateCloudQueueClient(StorageClientFactoryContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException("context");
            }
            return context.Account.CreateCloudQueueClient();
        }
    }
}
