// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;

#if PUBLICSTORAGE
using Microsoft.Azure.WebJobs.Storage.Blob;
using Microsoft.Azure.WebJobs.Storage.Queue;
using Microsoft.Azure.WebJobs.Storage.Table;
#else
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
    /// <summary>Represents a storage account.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageAccount : IStorageAccount
#else
    internal class StorageAccount : IStorageAccount
#endif
    {
        private readonly CloudStorageAccount _sdkAccount;

        /// <summary>Initializes a new instance of the <see cref="StorageAccount"/> class.</summary>
        /// <param name="sdkAccount">The underlying SDK cloud storage account.</param>
        public StorageAccount(CloudStorageAccount sdkAccount)
        {
            if (sdkAccount == null)
            {
                throw new ArgumentNullException("sdkAccount");
            }

            _sdkAccount = sdkAccount;
        }

        /// <inheritdoc />
        public Uri BlobEndpoint
        {
            get { return _sdkAccount.BlobEndpoint; }
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdkAccount.Credentials; }
        }

        /// <inheritdoc />
        public CloudStorageAccount SdkObject
        {
            get { return _sdkAccount; }
        }

        /// <inheritdoc />
        public IStorageBlobClient CreateBlobClient()
        {
            CloudBlobClient sdkClient = _sdkAccount.CreateCloudBlobClient();
            return new StorageBlobClient(sdkClient);
        }

        /// <inheritdoc />
        public IStorageQueueClient CreateQueueClient()
        {
            CloudQueueClient sdkClient = _sdkAccount.CreateCloudQueueClient();
            return new StorageQueueClient(sdkClient);
        }

        /// <inheritdoc />
        public IStorageTableClient CreateTableClient()
        {
            CloudTableClient sdkClient = _sdkAccount.CreateCloudTableClient();
            return new StorageTableClient(sdkClient);
        }

        /// <inheritdoc />
        public string ToString(bool exportSecrets)
        {
            return _sdkAccount.ToString(exportSecrets: exportSecrets);
        }
    }
}
