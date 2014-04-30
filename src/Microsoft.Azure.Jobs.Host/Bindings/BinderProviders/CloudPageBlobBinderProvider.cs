using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal class CloudPageBlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(CloudPageBlob))
            {
                return new CloudPageBlobBinder();
            }

            return null;
        }

        private class CloudPageBlobBinder : CloudBlobBinder
        {
            protected override object BindCore(CloudBlobContainer container, string blobName)
            {
                return container.GetPageBlobReference(blobName);
            }
        }
    }
}
