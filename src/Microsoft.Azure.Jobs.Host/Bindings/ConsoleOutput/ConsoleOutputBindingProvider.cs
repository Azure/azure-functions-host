using System.IO;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.ConsoleOutput
{
    internal class ConsoleOutputBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (parameter.ParameterType != typeof(TextWriter))
            {
                return null;
            }

            return new ConsoleOutputBinding(parameter.Name);
        }
    }
}
