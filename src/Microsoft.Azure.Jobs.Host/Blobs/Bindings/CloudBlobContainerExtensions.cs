// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal static class CloudBlobContainerExtensions
    {
        public static Task<ICloudBlob> GetBlobReferenceForArgumentTypeAsync(this CloudBlobContainer container,
            string blobName, Type argumentType, CancellationToken cancellationToken)
        {

            if (argumentType == typeof(CloudBlockBlob))
            {
                ICloudBlob blob = container.GetBlockBlobReference(blobName);
                return Task.FromResult(blob);
            }
            else if (argumentType == typeof(CloudPageBlob))
            {
                ICloudBlob blob = container.GetPageBlobReference(blobName);
                return Task.FromResult(blob);
            }
            else
            {
                return GetExistingOrNewBlockBlobReferenceAsync(container, blobName, cancellationToken);
            }
        }

        private static async Task<ICloudBlob> GetExistingOrNewBlockBlobReferenceAsync(this CloudBlobContainer container,
            string blobName, CancellationToken cancellationToken)
        {
            try
            {
                return await container.GetBlobReferenceFromServerAsync(blobName, cancellationToken);
            }
            catch (StorageException exception)
            {
                RequestResult result = exception.RequestInformation;

                if (result == null || result.HttpStatusCode != 404)
                {
                    throw;
                }
                else
                {
                    return container.GetBlockBlobReference(blobName);
                }
            }
        }
    }
}
