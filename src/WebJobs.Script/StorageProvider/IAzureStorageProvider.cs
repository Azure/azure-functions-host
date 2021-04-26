// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Storage.Blobs;

namespace Microsoft.Azure.WebJobs.Script
{
    public interface IAzureStorageProvider
    {
        bool TryGetBlobServiceClientFromConnectionString(out BlobServiceClient client, string connectionString);

        bool TryGetBlobServiceClientFromConnection(out BlobServiceClient client, string connection);

        BlobContainerClient GetBlobContainerClient();
    }
}
