// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobToCloudPageBlobConverter : IConverter<ICloudBlob, CloudPageBlob>
    {
        public CloudPageBlob Convert(ICloudBlob input)
        {
            CloudPageBlob pageBlob = input as CloudPageBlob;

            if (pageBlob == null)
            {
                throw new InvalidOperationException("The blob is not a page blob");
            }

            return pageBlob;
        }
    }
}
