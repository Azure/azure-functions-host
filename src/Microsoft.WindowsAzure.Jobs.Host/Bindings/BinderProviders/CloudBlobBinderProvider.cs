using System;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs.Host.Bindings.BinderProviders
{
    // Binder provider for the ICloudBlob SDK type
    internal class CloudBlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(ICloudBlob))
            {
                return isInput ? (ICloudBlobBinder)new InputICloudBlobBinder() : new OutputICloudBlobBinder();
            }

            return null;
        }

        private class InputICloudBlobBinder : CloudBlobBinder
        {
            protected override object BindCore(CloudBlobContainer container, string blobName)
            {
                return container.GetBlobReferenceFromServer(blobName);
            }
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
