using System.Reflection;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class CancellationTokenStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(CancellationToken))
            {
                return new CancellationTokenParameterStaticBinding { Name = parameter.Name };
            }

            return null;
        }
    }
}
