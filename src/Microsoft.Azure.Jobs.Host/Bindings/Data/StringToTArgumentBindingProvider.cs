using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class StringToTArgumentBindingProvider<TBindingData> : IDataArgumentBindingProvider<TBindingData>
    {
        public IArgumentBinding<TBindingData> TryCreate(ParameterInfo parameter)
        {
            if (typeof(TBindingData) != typeof(string))
            {
                return null;
            }

            Type parameterType = parameter.ParameterType;

            if (!ObjectBinderHelpers.CanBindFromString(parameterType))
            {
                return null;
            }

            return new StringToTArgumentBinding(parameterType);
        }

        private class StringToTArgumentBinding : IArgumentBinding<TBindingData>
        {
            private readonly Type _valueType;

            public StringToTArgumentBinding(Type valueType)
            {
                _valueType = valueType;
            }

            public Type ValueType
            {
                get { return _valueType; }
            }

            public IValueProvider Bind(TBindingData value, ArgumentBindingContext context)
            {
                string text = value.ToString(); // Really (string)input, but the compiler can't verify that's possible.
                object converted = ObjectBinderHelpers.BindFromString(text, _valueType);
                return new ObjectValueProvider(converted, _valueType);
            }
        }
    }
}
