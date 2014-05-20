using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal abstract class CloudBlobBinder : ICloudBlobBinder
    {
        public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
        {
            CloudStorageAccount account = Utility.GetAccount(binder.StorageConnectionString);
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(containerName);
            container.CreateIfNotExists();

            var blob = BindCore(container, blobName);
            return new BindResult { Result = blob };
        }

        protected abstract object BindCore(CloudBlobContainer container, string blobName);
    }
}
