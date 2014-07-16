// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System;

namespace Microsoft.Azure.Jobs.Host.Blobs.Bindings
{
    internal static class CloudBlobContainerExtensions
    {
        public static ICloudBlob GetBlobReferenceForArgumentType(this CloudBlobContainer container, string blobName, Type argumentType)
        {
            if (argumentType == typeof(CloudBlockBlob))
            {
                return container.GetBlockBlobReference(blobName);
            }
            else if (argumentType == typeof(CloudPageBlob))
            {
                return container.GetPageBlobReference(blobName);
            }
            else
            {
                return GetExistingOrNewBlockBlobReference(container, blobName);
            }
        }

        private static ICloudBlob GetExistingOrNewBlockBlobReference(this CloudBlobContainer container, string blobName)
        {
            try
            {
                return container.GetBlobReferenceFromServer(blobName);
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
