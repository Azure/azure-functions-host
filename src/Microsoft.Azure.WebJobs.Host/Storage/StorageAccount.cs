// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Storage
{
    internal class StorageAccount : IStorageAccount
    {
        private readonly CloudStorageAccount _sdkAccount;
        private readonly IServiceProvider _services;

        /// <summary>
        /// Initializes a new instance of the <see cref="StorageAccount"/> class.
        /// </summary>
        /// <param name="sdkAccount">The underlying SDK cloud storage account.</param>
        /// <param name="services">The <see cref="IServiceProvider"/> to use.</param>
        public StorageAccount(CloudStorageAccount sdkAccount, IServiceProvider services)
        {
            if (sdkAccount == null)
            {
                throw new ArgumentNullException("sdkAccount");
            }
            if (services == null)
            {
                throw new ArgumentNullException("services");
            }

            _sdkAccount = sdkAccount;
            _services = services;
            Type = StorageAccountType.GeneralPurpose;
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
        public StorageAccountType Type { get; set; }

        private StorageClientFactory ClientFactory
        {
            get
            {
                return (StorageClientFactory)_services.GetService(typeof(StorageClientFactory));
            }
        }

        /// <inheritdoc />
        public IStorageBlobClient CreateBlobClient(StorageClientFactoryContext context = null)
        {
            context = DefaultContext(context);

            CloudBlobClient sdkClient = ClientFactory.CreateCloudBlobClient(context);
            return new StorageBlobClient(sdkClient);
        }

        /// <inheritdoc />
        public IStorageQueueClient CreateQueueClient(StorageClientFactoryContext context = null)
        {
            context = DefaultContext(context);

            CloudQueueClient sdkClient = ClientFactory.CreateCloudQueueClient(context);
            return new StorageQueueClient(sdkClient);
        }

        /// <inheritdoc />
        public IStorageTableClient CreateTableClient(StorageClientFactoryContext context = null)
        {
            context = DefaultContext(context);

            CloudTableClient sdkClient = ClientFactory.CreateCloudTableClient(context);
            return new StorageTableClient(sdkClient);
        }

        /// <inheritdoc />
        public string ToString(bool exportSecrets)
        {
            return _sdkAccount.ToString(exportSecrets: exportSecrets);
        }

        private StorageClientFactoryContext DefaultContext(StorageClientFactoryContext context)
        {
            if (context == null)
            {
                // if no context provided, provide the default
                context = new StorageClientFactoryContext();
            }

            // always set the account
            context.Account = _sdkAccount;

            return context;
        }
    }
}
