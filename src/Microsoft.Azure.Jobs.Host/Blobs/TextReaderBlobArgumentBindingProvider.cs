using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal class TextReaderBlobArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            if (parameterType != typeof(TextReader))
            {
                return null;
            }

            return new TextReaderArgumentBinding();
        }

        private class TextReaderArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            public Type ValueType
            {
                get { return typeof(TextReader); }
            }

            public IValueProvider Bind(ICloudBlob value, ArgumentBindingContext context)
            {
                Stream rawStream = value.OpenRead();
                WatchableStream watchableStream = new WatchableStream(value.OpenRead(), value.Properties.Length);
                TextReader reader = new StreamReader(watchableStream);
                return new BlobWatchableDisposableValueProvider(value, reader, typeof(TextReader),
                    watcher: watchableStream, disposable: reader);
            }
        }
    }
}
