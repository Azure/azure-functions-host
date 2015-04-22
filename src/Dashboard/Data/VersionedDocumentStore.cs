// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class VersionedDocumentStore
    {
        [CLSCompliant(false)]
        public static IVersionedDocumentStore<TDocument> CreateJsonBlobStore<TDocument>(CloudBlobClient client,
            string containerName, string directoryName)
        {
            IVersionedMetadataTextStore innerStore = VersionedTextStore.CreateBlobStore(client, containerName,
                directoryName);
            return new JsonVersionedDocumentStore<TDocument>(innerStore);
        }
    }
}
