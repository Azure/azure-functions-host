using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class RuntimeBinding : IBinding
    {
        private readonly string _parameterName;

        public RuntimeBinding(string parameterName)
        {
            _parameterName = parameterName;
        }

        public bool FromAttribute
        {
            get { return false; }
        }

        private IValueProvider Bind(IAttributeBinding binding, FunctionBindingContext context)
        {
            return new RuntimeValueProvider(binding);
        }

        public IValueProvider Bind(object value, FunctionBindingContext context)
        {
            IAttributeBinding binding = value as IAttributeBinding;

            if (binding == null)
            {
                throw new InvalidOperationException("Unable to convert value to IAttributeBinding.");
            }

            return Bind(binding, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(new AttributeBinding(context), context.FunctionContext);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BinderParameterDescriptor
            {
                Name = _parameterName
            };
        }
    }
}
