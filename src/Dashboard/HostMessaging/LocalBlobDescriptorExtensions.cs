// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.HostMessaging
{
    internal static class LocalBlobDescriptorExtensions
    {
        public static CloudBlockBlob GetBlockBlob(this LocalBlobDescriptor descriptor, string connectionString)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException("descriptor");
            }

            if (connectionString == null)
            {
                return null;
            }

            CloudStorageAccount account = CloudStorageAccount.Parse(connectionString);
            return GetBlockBlob(descriptor, account);
        }

        public static CloudBlockBlob GetBlockBlob(this LocalBlobDescriptor descriptor, CloudStorageAccount account)
        {
            if (descriptor == null)
            {
                throw new ArgumentNullException("descriptor");
            }

            if (account == null)
            {
                return null;
            }

            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(descriptor.ContainerName);
            return container.GetBlockBlobReference(descriptor.BlobName);
        }
    }
}
