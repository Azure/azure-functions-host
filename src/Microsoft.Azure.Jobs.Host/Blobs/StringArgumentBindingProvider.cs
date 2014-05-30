using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Triggers
{
    internal class StringBlobArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            if (parameterType != typeof(string))
            {
                return null;
            }

            return new StringArgumentBinding();
        }

        private class StringArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            public Type ValueType
            {
                get { return typeof(string); }
            }

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                string value;
                string status;

                using (Stream rawStream = blob.OpenRead())
                using (WatchableStream watchableStream = new WatchableStream(rawStream, blob.Properties.Length))
                using (TextReader reader = new StreamReader(watchableStream))
                {
                    value = reader.ReadToEnd();
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, typeof(string), new StaticSelfWatch(status));
            }
        }
    }
}
