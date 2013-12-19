using System;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
{
    class BlobStreamBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobStreamBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                Stream blobStream = IsInput ? blob.OpenRead() : blob.OpenWrite();

                if (IsInput)
                {
                    blob.FetchAttributes();
                    long length = blob.Properties.Length;
                    blobStream = new WatchableStream(blobStream, length);
                }
                else
                {
                    blobStream = new WatchableStream(blobStream);
                }

                return new BindCleanupResult
                {
                    Result = blobStream,
                    Cleanup = () => blobStream.Close()
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(Stream))
            {
                return new BlobStreamBinder { IsInput = isInput };
            }
            return null;
        }
    }
}
