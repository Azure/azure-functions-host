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
