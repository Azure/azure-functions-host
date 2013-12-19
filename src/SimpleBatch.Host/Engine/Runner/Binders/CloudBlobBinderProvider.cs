using System;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    // Bind directly to a CloudBlob. 
    class CloudBlobBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                return new BindCleanupResult
                {
                    Result = blob                    
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(CloudBlob))
            {
                return new BlobBinder { IsInput = isInput };
            }
            return null;
        }
    }
}
