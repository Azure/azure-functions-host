// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a blob directory.</summary>
#if PUBLICSTORAGE
    
    public class StorageBlobDirectory : IStorageBlobDirectory
#else
    internal class StorageBlobDirectory : IStorageBlobDirectory
#endif
    {
        private readonly IStorageBlobContainer _parent;
        private readonly CloudBlobDirectory _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobDirectory"/> class.</summary>
        /// <param name="parent">The parent blob container.</param>
        /// <param name="sdk">The SDK directory to wrap.</param>
        public StorageBlobDirectory(IStorageBlobContainer parent, CloudBlobDirectory sdk)
        {
            _parent = parent;
            _sdk = sdk;
        }

        /// <inheritdoc />
        public IStorageBlobContainer Container
        {
            get { return _parent; }
        }

        /// <inheritdoc />
        public IStorageBlobClient ServiceClient
        {
            get { return _parent.ServiceClient; }
        }

        /// <inheritdoc />
        public IStorageBlockBlob GetBlockBlobReference(string blobName)
        {
            CloudBlockBlob sdkBlob = _sdk.GetBlockBlobReference(blobName);
            return new StorageBlockBlob(_parent, sdkBlob);
        }
    }
}
