// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public static class BlobCausalityReader
    {
        public static Guid? GetParentId(ICloudBlob blob)
        {
            if (!blob.TryFetchAttributes())
            {
                return null;
            }

            if (!blob.Metadata.ContainsKey(BlobMetadataKeys.ParentId))
            {
                return null;
            }

            string val = blob.Metadata[BlobMetadataKeys.ParentId];
            Guid result;
            if (Guid.TryParse(val, out result))
            {
                return result;
            }

            return null;
        }
    }
}