using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ConverterBlobArgumentBindingProvider<T> : IBlobArgumentBindingProvider
    {
        private readonly IConverter<ICloudBlob, T> _converter;

        public ConverterBlobArgumentBindingProvider(IConverter<ICloudBlob, T> converter)
        {
            _converter = converter;
        }

        public IArgumentBinding<ICloudBlob> TryCreate(Type parameterType)
        {
            if (parameterType != typeof(T))
            {
                return null;
            }

            return new ConverterArgumentBinding(_converter);
        }

        private class ConverterArgumentBinding : IArgumentBinding<ICloudBlob>
        {
            private readonly IConverter<ICloudBlob, T> _converter;

            public ConverterArgumentBinding(IConverter<ICloudBlob, T> converter)
            {
                _converter = converter;
            }

            public Type ValueType
            {
                get { return typeof(T); }
            }

            public IValueProvider Bind(ICloudBlob value)
            {
                return new BlobValueProvider(value, _converter.Convert(value), typeof(T));
            }
        }
    }
}
