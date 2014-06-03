using System;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Runtime
{
    internal class RuntimeBinding : IBinding
    {
        private IValueProvider Bind(IAttributeBinding binding, ArgumentBindingContext context)
        {
            return new RuntimeValueProvider(binding);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
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
            return Bind(new AttributeBinding(context), context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new BinderParameterDescriptor();
        }
    }
}
