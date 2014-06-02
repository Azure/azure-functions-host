using System;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs
{
    internal class ConverterArgumentBindingProvider<T> : IBlobArgumentBindingProvider
    {
        private readonly IConverter<ICloudBlob, T> _converter;

        public ConverterArgumentBindingProvider(IConverter<ICloudBlob, T> converter)
        {
            _converter = converter;
        }

        public IArgumentBinding<ICloudBlob> TryCreate(ParameterInfo parameter)
        {
            if (parameter.ParameterType != typeof(T))
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

            public IValueProvider Bind(ICloudBlob value, ArgumentBindingContext context)
            {
                return new BlobValueProvider(value, _converter.Convert(value), typeof(T));
            }
        }
    }
}
