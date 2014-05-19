using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class ConsoleOutputStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(TextWriter))
            {
                return new ConsoleOutputParameterStaticBinding { Name = parameter.Name };
            }

            return null;
        }
    }
}
