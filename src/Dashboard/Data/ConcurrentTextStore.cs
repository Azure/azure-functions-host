// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class ConcurrentTextStore
    {
        [CLSCompliant(false)]
        public static IConcurrentMetadataTextStore CreateBlobStore(CloudBlobClient client, string containerName,
            string directoryName)
        {
            CloudBlobContainer container = client.GetContainerReference(containerName);
            CloudBlobDirectory directory = container.GetDirectoryReference(directoryName);
            return new BlobConcurrentTextStore(directory);
        }
    }
}
