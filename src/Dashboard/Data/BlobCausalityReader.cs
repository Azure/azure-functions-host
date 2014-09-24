// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Dashboard.Data
{
    public class BlobCausalityReader
    {
        [CLSCompliant(false)]
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