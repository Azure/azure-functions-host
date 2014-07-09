using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class TextReaderArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(TextReader))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to TextReader using access "
                    + access.Value.ToString() + ".");
            }

            return new TextReaderArgumentBinding();
        }

        private class TextReaderArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(TextReader); }
            }

            public IValueProvider Bind(ICloudBlob blob, FunctionBindingContext context)
            {
                Stream rawStream;

                try
                {
                    rawStream = blob.OpenRead();
                }
                catch (StorageException exception)
                {
                    if (exception.IsNotFound())
                    {
                        return new BlobValueProvider(blob, null, typeof(TextReader));
                    }
                    else
                    {
                        throw;
                    }
                }

                WatchableReadStream watchableStream = new WatchableReadStream(rawStream);
                TextReader reader = new StreamReader(watchableStream);
                return new BlobWatchableDisposableValueProvider(blob, reader, typeof(TextReader),
                    watcher: watchableStream, disposable: reader);
            }
        }
    }
}
