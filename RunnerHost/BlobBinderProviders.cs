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
            public BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType)
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

    class BlobStreamBinderProvider : ICloudBlobBinderProvider
    {
        private class BlobStreamBinder : ICloudBlobBinder
        {
            public bool IsInput;
            public BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType)
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
            TextWriter _content = new StringWriter();
            public BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

                return new BindCleanupResult
                {
                     Result =_content,
                     Cleanup = () => blob.UploadText(_content.ToString())
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
            public BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType)
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