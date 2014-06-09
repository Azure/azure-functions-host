using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
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
