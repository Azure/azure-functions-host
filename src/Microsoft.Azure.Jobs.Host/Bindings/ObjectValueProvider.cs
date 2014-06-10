using System;

namespace Microsoft.Azure.Jobs.Host.Bindings
{
    internal class ObjectValueProvider : IValueProvider
    {
        private readonly object _value;
        private readonly Type _valueType;

        public ObjectValueProvider(object value, Type valueType)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _value = value;
            _valueType = valueType;
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            return _value != null ? _value.ToString() : null;
        }
    }
}
