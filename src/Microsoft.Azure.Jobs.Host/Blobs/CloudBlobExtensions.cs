using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal static class CloudBlobExtensions
    {
        public static string GetBlobPath(this ICloudBlob blob)
        {
            if (blob == null)
            {
                throw new ArgumentNullException("blob");
            }

            return blob.Container.Name + "/" + blob.Name;
        }
    }
}
