using System;
using System.IO;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // For writing back to the blob
    // Blobs are connectionless and don't support incremental writes, so buffer the write locally
    // and then push at the end.
    class TextWriterProvider : ICloudBlobBinderProvider
    {
        private class TextWriterBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                MemoryStream ms = new MemoryStream();
                WatchableStream watcher = new WatchableStream(ms);
                TextWriter _content = new StreamWriter(watcher);

                ICloudBlob blob = BlobClient.GetBlob(binder.StorageConnectionString, containerName, blobName);

                return new BindCleanupResult
                {
                    Result = _content,
                    SelfWatch = watcher,
                    Cleanup = () =>
                    {
                        using (_content)
                        {
                            if (ms.CanWrite)
                            {
                                _content.Flush();
                            }

                            if (watcher.Complete())
                            {
                                var bytes = ms.ToArray();
                                blob.UploadFromByteArray(bytes, 0, bytes.Length);
                            }
                        }
                    }
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(TextWriter))
            {
                return new TextWriterBinder();
            }
            return null;
        }
    }
}
