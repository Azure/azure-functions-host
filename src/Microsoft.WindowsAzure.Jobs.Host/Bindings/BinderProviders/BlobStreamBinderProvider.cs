using System;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs.Host.Bindings.BinderProviders
{
    class BlobStreamBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobStreamBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlockBlob blob = BlobClient.GetBlockBlob(binder.AccountConnectionString, containerName, blobName);

                CloudBlobStream writeableBlobStream;
                WatchableStream watchableBlobStream;

                if (IsInput)
                {
                    Stream blobStream = blob.OpenRead();
                    writeableBlobStream = null;
                    blob.FetchAttributes();
                    long length = blob.Properties.Length;
                    watchableBlobStream = new WatchableStream(blobStream, length);
                }
                else
                {
                    writeableBlobStream = blob.OpenWrite();
                    watchableBlobStream = new WatchableStream(writeableBlobStream);
                }


                return new BindCleanupResult
                {
                    Result = watchableBlobStream,
                    Cleanup = () =>
                    {
                        if (writeableBlobStream != null)
                        {
                            using (watchableBlobStream)
                            {
                            }
                        }
                        else if (watchableBlobStream.Complete())
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
