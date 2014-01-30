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

                BlobStream originalBlobStream = IsInput ? blob.OpenRead() : blob.OpenWrite();
                WatchableStream watchableBlobStream;

                if (IsInput)
                {
                    blob.FetchAttributes();
                    long length = blob.Properties.Length;
                    watchableBlobStream = new WatchableStream(originalBlobStream, length);
                }
                else
                {
                    watchableBlobStream = new WatchableStream(originalBlobStream);
                }

                return new BindCleanupResult
                {
                    Result = watchableBlobStream,
                    Cleanup = () =>
                    {
                        using (watchableBlobStream)
                        {
                            if (!watchableBlobStream.Complete())
                            {
                                originalBlobStream.Abort();
                            }
                        }
                    }
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
