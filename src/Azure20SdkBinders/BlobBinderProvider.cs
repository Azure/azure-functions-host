using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using SimpleBatch;

namespace Azure20SdkBinders
{
    class BlobBinderProvider : ICloudBlobBinderProvider
    {
        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)        
        {
            if (Utility.IsAzureSdk20Type(targetType))
            {
                return TryGetBinderWorker(targetType, isInput);
            }
            return null;
        }

        public ICloudBlobBinder TryGetBinderWorker(Type targetType, bool isInput)
        {
            Func<CloudBlobContainer, string, object> func = null;

            if (targetType == typeof(ICloudBlob))
            {
                if (isInput)
                {
                    // Assumes it's on the server. So must be reader
                    func = (container, blobName) => container.GetBlobReferenceFromServer(blobName);
                }
                else
                {
                    // Writer. Default to Block blobs.
                    func = (container, blobName) => container.GetBlockBlobReference(blobName);
                }
            }

            // $$$ Check blob/Page compatability?
            if (targetType == typeof(CloudPageBlob))
            {
                func = (container, blobName) => container.GetPageBlobReference(blobName);
            }

            if (targetType == typeof(CloudBlockBlob))
            {
                func = (container, blobName) => container.GetBlockBlobReference(blobName);
            }

            if (func == null)
            {
                return null;
            }
            return new Azure20SdkBlobBinder { _func = func };            
        }
    }

    class Azure20SdkBlobBinder : ICloudBlobBinder
    {
        public Func<CloudBlobContainer, string, object> _func;

        public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
        {
            CloudStorageAccount account = binder.GetAccount();
            CloudBlobClient client = account.CreateCloudBlobClient();
            CloudBlobContainer container = client.GetContainerReference(containerName);
            container.CreateIfNotExists();

            var blob = _func(container, blobName);
            return new BindResult { Result = blob };            
        }

    }
}
