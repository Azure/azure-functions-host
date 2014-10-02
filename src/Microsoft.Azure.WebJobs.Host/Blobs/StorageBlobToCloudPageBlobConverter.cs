// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class StorageBlobToCloudPageBlobConverter : IConverter<IStorageBlob, CloudPageBlob>
    {
        public CloudPageBlob Convert(IStorageBlob input)
        {
            if (input == null)
            {
                return null;
            }

            IStoragePageBlob pageBlob = input as IStoragePageBlob;

            if (pageBlob == null)
            {
                throw new InvalidOperationException("The blob is not a page blob.");
            }

            return pageBlob.SdkObject;
        }
    }
}
