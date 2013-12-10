using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Bind directly to a CloudBlob. 
    class CloudBlobBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

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

    // 2-way serialize as a rich object. 
    // Uses leases.
    class JsonByRefBlobBinder : ICloudBlobBinder
    {
        public IBlobLeaseHolder Lease { get; set; }

        public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
        {
            CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);


            object result;
            string json = string.Empty;
            if (Utility.DoesBlobExist(blob))
            {
                // If blob was just created, it may be 0-length, which is not valid JSON.
                json = blob.DownloadText();
            }

            if (!string.IsNullOrWhiteSpace(json))
            {
                result = ObjectBinderHelpers.DeserializeObject(json, targetType);
            }
            else
            {
                result = NewDefault(targetType);
            }
            
            var x = new BindCleanupResult
            {
                Result = result
            };

            // $$$ There's still a far distance between here and releasing the lease in BindResult.Cleanup. 
            // Could have an orphaned lease. 
            var newLease = Lease.TransferOwnership();

            x.Cleanup = () =>
                {
                    object newResult = x.Result;
                    using (newLease)
                    {
                        string json2 = ObjectBinderHelpers.SerializeObject(newResult, targetType);
                        newLease.UploadText(json2);
                    }                    
                };
            return x;
        }

        static object NewDefault(Type targetType)
        {
            if (targetType.IsClass)
            {
                return null;
            }
            return Activator.CreateInstance(targetType);
        }
    }

    class BlobStreamBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobStreamBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

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

                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

                return new BindCleanupResult
                {
                    Result = _content,
                    SelfWatch = watcher, 
                    Cleanup = () =>
                        {
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

    class TextReaderProvider : ICloudBlobBinderProvider
    {
        private class TextReaderBinder : ICloudBlobBinder
        {
            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

                long length = blob.Properties.Length;
                var blobStream = blob.OpenRead();
                var streamWatcher = new WatchableStream(blobStream, length);
                var textReader = new StreamReader(streamWatcher);

                return new BindCleanupResult
                {
                    Result = textReader,
                    SelfWatch = streamWatcher, 
                    Cleanup = () => textReader.Close()
                };
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (targetType == typeof(TextReader))
            {
                return new TextReaderBinder();
            }
            return null;
        }
    }
}