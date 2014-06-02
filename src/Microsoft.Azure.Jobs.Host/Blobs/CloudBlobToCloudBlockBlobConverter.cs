using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class CloudBlobToCloudBlockBlobConverter : IConverter<ICloudBlob, CloudBlockBlob>
    {
        public CloudBlockBlob Convert(ICloudBlob input)
        {
            CloudBlockBlob blockBlob = input as CloudBlockBlob;

            if (blockBlob == null)
            {
                throw new InvalidOperationException("The blob is not a block blob");
            }

            return blockBlob;
        }
    }
}
