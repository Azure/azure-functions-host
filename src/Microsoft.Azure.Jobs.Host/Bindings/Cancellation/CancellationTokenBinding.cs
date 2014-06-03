using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.Cancellation
{
    internal class CancellationTokenBinding : IBinding
    {
        private IValueProvider Bind(CancellationToken token, ArgumentBindingContext context)
        {
            return new CancellationTokenValueProvider(token);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            if (value is CancellationToken)
            {
                throw new InvalidOperationException("Unable to convert value to CancellationToken.");
            }

            CancellationToken token = (CancellationToken)value;

            return Bind(token, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.CancellationToken, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new CancellationTokenParameterDescriptor();
        }
    }
}
