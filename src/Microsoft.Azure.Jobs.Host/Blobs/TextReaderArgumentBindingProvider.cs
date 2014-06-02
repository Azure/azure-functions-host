using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class TextReaderArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(TextReader))
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

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                Stream rawStream = blob.OpenRead();
                SelfWatchReadStream selfWatchStream = new SelfWatchReadStream(blob.OpenRead());
                TextReader reader = new StreamReader(selfWatchStream);
                return new BlobWatchableDisposableValueProvider(blob, reader, typeof(TextReader),
                    watcher: selfWatchStream, disposable: reader);
            }
        }
    }
}
