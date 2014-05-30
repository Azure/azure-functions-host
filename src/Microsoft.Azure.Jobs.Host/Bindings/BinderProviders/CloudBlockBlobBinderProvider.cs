using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal class CloudBlockBlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(CloudBlockBlob))
            {
                return new CloudBlockBlobBinder();
            }

            return null;
        }

        private class CloudBlockBlobBinder : CloudBlobBinder
        {
            protected override object BindCore(CloudBlobContainer container, string blobName)
            {
                return container.GetBlockBlobReference(blobName);
            }
        }
    }
}
