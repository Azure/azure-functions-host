// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// TODO: TEMP - implementation should be moved https://github.com/Azure/azure-webjobs-sdk/issues/2710
    /// Interface to retrieve BlobServiceClient objects
    /// </summary>
    public interface IAzureStorageProvider
    {
        bool ConnectionExists(string connection);

        bool TryGetBlobServiceClientFromConnectionString(out BlobServiceClient client, string connectionString);

        bool TryGetBlobServiceClientFromConnection(out BlobServiceClient client, string connection);

        BlobContainerClient GetBlobContainerClient();
    }
}
