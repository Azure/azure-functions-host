using System;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    class BlobStreamBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobStreamBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlockBlob blob = BlobClient.GetBlockBlob(binder.StorageConnectionString, containerName, blobName);

                CloudBlobStream writeableBlobStream = blob.OpenWrite();
                WatchableStream watchableBlobStream = new WatchableStream(writeableBlobStream);

                return new BindCleanupResult
                {
                    Result = watchableBlobStream,
                    Cleanup = () =>
                    {
                        if (watchableBlobStream.Complete())
                        {
                            using (watchableBlobStream)
                            {
                                writeableBlobStream.Commit();
                            }
                        }

                        // Don't dispose a writeableBlobStream that hasn't completed, or it will auto-commit.
                        // Unfortunately, there's no way to dispose of its resources deterministically without
                        // committing.
                    }
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(Stream))
            {
                return new BlobStreamBinder();
            }
            return null;
        }
    }
}
