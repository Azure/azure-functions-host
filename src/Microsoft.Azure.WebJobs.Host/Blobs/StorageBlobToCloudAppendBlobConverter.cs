// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs
{
    internal class StorageBlobToCloudAppendBlobConverter : IConverter<IStorageBlob, CloudAppendBlob>
    {
        public CloudAppendBlob Convert(IStorageBlob input)
        {
            if (input == null)
            {
                return null;
            }

            IStorageAppendBlob appendBlob = input as IStorageAppendBlob;

            if (appendBlob == null)
            {
                throw new InvalidOperationException("The blob is not an append blob.");
            }

            return appendBlob.SdkObject;
        }
    }
}
