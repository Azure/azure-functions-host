using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StreamArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(Stream))
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

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                Stream rawStream = blob.OpenRead();
                using (SelfWatchReadStream selfWatchStream = new SelfWatchReadStream(rawStream))
                return new BlobWatchableDisposableValueProvider(blob, selfWatchStream, typeof(Stream),
                    watcher: selfWatchStream, disposable: selfWatchStream);
            }
        }
    }
}
