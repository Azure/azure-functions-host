// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

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
