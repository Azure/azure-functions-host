using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using DataAccess;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
{
    // Bind IEnumerable<T> using a CSV reader and strong model binder
    class EnumerableBlobBinderProvider : ICloudBlobBinderProvider
    {
        private class EnumerableBlobBinder : ICloudBlobBinder
        {
            public Type ElementType;

            public BindResult Bind(IBinder binder, string containerName, string blobName, Type targetType)
            {
                CloudBlob blob = Utility.GetBlob(binder.AccountConnectionString, containerName, blobName);

                // RowAs<T> doesn't take System.Type, so need to use some reflection. 
                Stream blobStream = blob.OpenRead();
                var data = DataTable.New.ReadLazy(blobStream);

                var mi = data.GetType().GetMethod("RowsAs");
                var mi2 = mi.MakeGenericMethod(ElementType);

                object result = mi2.Invoke(data, new object[0]);
                return new BindCleanupResult
                { 
                    Result = result,
                    Cleanup = () => blobStream.Close()
                } ;
            }
        }

        public ICloudBlobBinder TryGetBinder(Type targetType, bool isInput)
        {
            if (!isInput)
            {
                return null;
            }
            var rowType = Program.IsIEnumerableT(targetType);
            if (rowType != null)
            {
                return new EnumerableBlobBinder { ElementType = rowType };
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