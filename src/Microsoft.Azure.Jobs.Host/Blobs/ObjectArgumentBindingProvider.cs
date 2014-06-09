using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ObjectArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly ICloudBlobStreamObjectBinder _objectBinder;
        private readonly Type _valueType;

        public ObjectArgumentBindingProvider(Type cloudBlobStreamBinderType)
        {
            _objectBinder = CloudBlobStreamObjectBinder.Create(cloudBlobStreamBinderType, out _valueType);
        }

        public ObjectArgumentBindingProvider(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
        {
            _objectBinder = objectBinder;
            _valueType = valueType;
        }

        public IBlobArgumentBinding TryCreate(ParameterInfo parameter, FileAccess? access)
        {
            if (parameter.ParameterType != _valueType)
            {
                return null;
            }

            if (access.HasValue && access.Value != FileAccess.Read)
            {
                throw new InvalidOperationException("Cannot bind blob to custom object using access "
                    + access.Value.ToString() + ".");
            }

            return new ObjectArgumentBinding(_objectBinder, _valueType);
        }

        private class ObjectArgumentBinding : IBlobArgumentBinding
        {
            private readonly ICloudBlobStreamObjectBinder _objectBinder;
            private readonly Type _valueType;

            public ObjectArgumentBinding(ICloudBlobStreamObjectBinder objectBinder, Type valueType)
            {
                _objectBinder = objectBinder;
                _valueType = valueType;
            }

            public FileAccess Access
            {
                get { return FileAccess.Read; }
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public IValueProvider Bind(ICloudBlob blob, ArgumentBindingContext context)
            {
                object value;
                string status;

                using (Stream rawStream = blob.OpenRead())
                using (SelfWatchReadStream selfWatchStream = new SelfWatchReadStream(rawStream))
                {
                    value = _objectBinder.ReadFromStream(selfWatchStream);
                    status = selfWatchStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, _valueType, new StaticSelfWatch(status));
            }
        }
    }
}
