using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            if (parameterType != typeof(Stream))
            {
                return null;
            }

            return new StreamArgumentBinding();
        }

        private class StreamArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            public Type ValueType
            {
                get { return typeof(Stream); }
            }

            public IValueProvider Bind(ICloudBlob value, ArgumentBindingContext context)
            {
                Stream rawStream = value.OpenRead();
                WatchableStream watchableStream = new WatchableStream(value.OpenRead(), value.Properties.Length);
                return new BlobWatchableDisposableValueProvider(value, watchableStream, typeof(Stream),
                    watcher: watchableStream, disposable: watchableStream);
            }
        }
    }
}
