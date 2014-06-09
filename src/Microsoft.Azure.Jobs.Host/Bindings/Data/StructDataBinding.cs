using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Data
{
    internal class StructDataBinding<TBindingData, TValue> : IBinding
        where TValue : struct
    {
        private static readonly IObjectToTypeConverter<TValue> _converter = new CompositeObjectToTypeConverter<TValue>(
            new StructOutputConverter<TValue, TValue>(new IdentityConverter<TValue>()),
            new ClassOutputConverter<string, TValue>(new StringToTConverter<TValue>()));

        private readonly string _parameterName;
        private readonly IArgumentBinding<TBindingData> _argumentBinding;

        public StructDataBinding(string parameterName, IArgumentBinding<TBindingData> argumentBinding)
        {
            _parameterName = parameterName;
            _argumentBinding = argumentBinding;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private IValueProvider Bind(TValue value, ArgumentBindingContext context)
        {
            return new ObjectValueProvider(value, typeof(TValue));
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            TValue typedValue = default(TValue);

            if (!_converter.TryConvert(value, out typedValue))
            {
                throw new InvalidOperationException("Unable to convert value to " + typeof(TValue).Name + ".");
            }

            return Bind(typedValue, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.BindingData[_parameterName], context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new RouteParameterDescriptor
            {
                Name = _parameterName
            };
        }
    }
}
