using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
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
