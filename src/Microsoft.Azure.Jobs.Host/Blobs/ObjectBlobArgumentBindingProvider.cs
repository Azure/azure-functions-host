using System;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ObjectBlobArgumentBindingProvider : IBlobArgumentBindingProvider
    {
        private readonly ITypelessCloudBlobStreamBinder _typelessBinder;
        private readonly Type _valueType;

        public ObjectBlobArgumentBindingProvider(Type cloudBlobStreamBinderType)
        {
            Type genericType = cloudBlobStreamBinderType.GetGenericTypeDefinition();
            _valueType = genericType.GetGenericArguments()[0];
            object innerBinder = Activator.CreateInstance(_valueType);
            Type typelessBinderType = typeof(TypelessCloudBlobStreamBinder<>).MakeGenericType(_valueType);
            _typelessBinder = (ITypelessCloudBlobStreamBinder)Activator.CreateInstance(typelessBinderType, innerBinder);
        }

        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            if (parameterType != _valueType)
            {
                return null;
            }

            return new ObjectArgumentBinding(_valueType, _typelessBinder);
        }

        private class ObjectArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            private readonly Type _valueType;
            private readonly ITypelessCloudBlobStreamBinder _typelessBinder;

            public ObjectArgumentBinding(Type valueType, ITypelessCloudBlobStreamBinder typelessBinder)
            {
                _valueType = valueType;
                _typelessBinder = typelessBinder;
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
                using (WatchableStream watchableStream = new WatchableStream(rawStream, blob.Properties.Length))
                {
                    value = _typelessBinder.ReadFromStream(watchableStream);
                    status = watchableStream.GetStatus();
                }

                return new BlobWatchableValueProvider(blob, value, _valueType, new StaticSelfWatch(status));
            }
        }

        private interface ITypelessCloudBlobStreamBinder
        {
            object ReadFromStream(Stream input);
        }

        private class TypelessCloudBlobStreamBinder<T>
        {
            private readonly ICloudBlobStreamBinder<T> _innerBinder;

            public TypelessCloudBlobStreamBinder(ICloudBlobStreamBinder<T> innerBinder)
            {
                _innerBinder = innerBinder;
            }

            public object ReadFromStream(Stream input)
            {
                return _innerBinder.ReadFromStream(input);
            }
        }
    }
}
