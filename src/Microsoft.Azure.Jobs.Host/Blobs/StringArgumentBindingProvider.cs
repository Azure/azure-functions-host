using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class StringArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        public IArgumentBinding<ICloudBlob> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(string))
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
                using (SelfWatchReadStream selfWatchStream = new SelfWatchReadStream(rawStream))
                using (TextReader reader = new StreamReader(selfWatchStream))
                {
                    value = reader.ReadToEnd();
                    status = selfWatchStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, typeof(string), new StaticSelfWatch(status));
            }
        }
    }
}
