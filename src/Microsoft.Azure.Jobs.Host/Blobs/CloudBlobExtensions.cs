// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal static class CloudBlobExtensions
    {
        public static string GetBlobPath(this ICloudBlob blob)
        {
            return ToBlobPath(blob).ToString();
        }

        public static BlobPath ToBlobPath(this ICloudBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            return new BlobPath(blob.Container.Name, blob.Name);
        }
    }
}
