// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class ConcurrentDocumentStore
    {
        public static IConcurrentMetadataDocumentStore<TDocument> CreateJsonBlobStore<TDocument>(CloudBlobClient client,
            string containerName, string directoryName)
        {
            IConcurrentMetadataTextStore innerStore = ConcurrentTextStore.CreateBlobStore(client, containerName, directoryName);
            return new JsonConcurrentDocumentStore<TDocument>(innerStore);
        }
    }
}
