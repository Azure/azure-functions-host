using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Binder provider for the ICloudBlob SDK type
    internal class CloudBlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(ICloudBlob))
            {
                return new OutputICloudBlobBinder();
            }

            return null;
        }

        private class OutputICloudBlobBinder : CloudBlobBinder
        {
            protected override object BindCore(CloudBlobContainer container, string blobName)
            {
                // Writer. Default to Block blobs.
                return container.GetBlockBlobReference(blobName);
            }
        }
    }
}
