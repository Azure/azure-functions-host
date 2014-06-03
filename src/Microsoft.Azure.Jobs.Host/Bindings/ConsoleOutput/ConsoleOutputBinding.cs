using System;
using System.IO;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBinding : IBinding
    {
        private IValueProvider Bind(TextWriter writer, ArgumentBindingContext context)
        {
            return new ConsoleOutputValueProvider(writer);
        }

        public IValueProvider Bind(object value, ArgumentBindingContext context)
        {
            TextWriter writer = value as TextWriter;

            if (writer == null)
            {
                throw new InvalidOperationException("Unable to convert value to console output TextWriter.");
            }

            return Bind(writer, context);
        }

        public IValueProvider Bind(BindingContext context)
        {
            return Bind(context.ConsoleOutput, context);
        }

        public ParameterDescriptor ToParameterDescriptor()
        {
            return new ConsoleOutputParameterDescriptor();
        }
    }
}
