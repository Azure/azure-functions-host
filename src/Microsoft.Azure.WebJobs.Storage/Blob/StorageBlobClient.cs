// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Blob
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Blob
#endif
{
    /// <summary>Represents a blob client.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageBlobClient : IStorageBlobClient
#else
    internal class StorageBlobClient : IStorageBlobClient
#endif
    {
        private readonly CloudBlobClient _sdk;

        /// <summary>Initializes a new instance of the <see cref="StorageBlobClient"/> class.</summary>
        /// <param name="sdk">The SDK client to wrap.</param>
        public StorageBlobClient(CloudBlobClient sdk)
        {
            _sdk = sdk;
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdk.Credentials; }
        }

        /// <inheritdoc />
        public IStorageBlobContainer GetContainerReference(string containerName)
        {
            CloudBlobContainer sdkContainer = _sdk.GetContainerReference(containerName);
            return new StorageBlobContainer(sdkContainer);
        }
    }
}
