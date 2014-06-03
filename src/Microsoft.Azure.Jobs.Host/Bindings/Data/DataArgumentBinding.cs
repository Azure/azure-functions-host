using System;
using Microsoft.Azure.Jobs.Host.Converters;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class DataArgumentBinding<TBindingData> : IArgumentBinding<TBindingData>
    {
        private readonly ITypeToObjectConverter<TBindingData> _converter;
        private readonly Type _valueType;

        public DataArgumentBinding(ITypeToObjectConverter<TBindingData> converter, Type valueType)
        {
            _converter = converter;
            _valueType = valueType;
        }

        public Type ValueType
        {
            get { return _valueType; }
        }

        public IValueProvider Bind(TBindingData value, ArgumentBindingContext context)
        {
            object converted = _converter.Convert(value);
            return new ObjectValueProvider(converted, _valueType);
        }
    }
}
