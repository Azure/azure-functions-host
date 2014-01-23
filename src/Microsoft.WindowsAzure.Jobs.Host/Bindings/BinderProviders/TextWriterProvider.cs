using System;
using System.IO;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs
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

                CloudBlob blob = BlobClient.GetBlob(binder.AccountConnectionString, containerName, blobName);

                return new BindCleanupResult
                {
                    Result = _content,
                    SelfWatch = watcher, 
                    Cleanup = () =>
                        {
                            if (ms.CanWrite)
                            {
                                _content.Flush();
                            }

                            if (ms.CanRead && ms.Length == 0)
                            {
                                // Don't upload unless the user either wrote at least one byte or explicitly closed the
                                // text writer.
                                _content.Dispose();
                                watcher.Dispose();
                                ms.Dispose();
                                return;
                            }

                            // _content was exposed to user, may already be flush/closed/disposed.
                            // But if it wasn't, we still need to flush so that the memory stream is current.
                            // flush() will fail if we're already closed, so call close. 
                            // But then close will close underlying streams, so MemoryStream becomes inaccessible.
                            _content.Close();

                            // Not accessible after calling close.
                            //ms.Position = 0; // reset to start
                            //blob.UploadFromStream(ms);

                            var bytes = ms.ToArray();
                            blob.UploadByteArray(bytes);                            
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
