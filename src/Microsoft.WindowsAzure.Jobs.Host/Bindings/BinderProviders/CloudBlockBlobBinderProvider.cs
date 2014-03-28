using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs.Host.Bindings.BinderProviders
{
    internal class CloudBlockBlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
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
