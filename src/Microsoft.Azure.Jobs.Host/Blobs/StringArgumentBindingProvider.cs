using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != typeof(string))
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to string using access "
                    + access.Value.ToString() + ".");
            }

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IBlobArgumentBinding
        {
            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return typeof(string); }
            }

            public IValueProvider Bind(ICloudBlob blob, FunctionBindingContext context)
            {
                string value;
                ParameterLog status;

                using (Stream rawStream = blob.OpenRead())
                using (WatchableReadStream watchableStream = new WatchableReadStream(rawStream))
                using (TextReader reader = new StreamReader(watchableStream))
                {
                    value = reader.ReadToEnd();
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, typeof(string), new ImmutableWatcher(status));
            }
        }
    }
}
