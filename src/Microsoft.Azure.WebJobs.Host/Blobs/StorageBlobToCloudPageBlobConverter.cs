// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
