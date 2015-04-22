// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal static class StorageBlockBlobExtensions
    {
        public static void UploadText(this IStorageBlockBlob blob, string content)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            blob.UploadTextAsync(content).GetAwaiter().GetResult();
        }
    }
}
