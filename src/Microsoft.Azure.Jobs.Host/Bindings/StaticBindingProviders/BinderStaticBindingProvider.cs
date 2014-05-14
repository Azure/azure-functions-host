using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class BinderStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter)
        {
            if (parameter.ParameterType == typeof(IBinder))
            {
                return new BinderParameterStaticBinding { Name = parameter.Name };
            }

            return null;
        }
    }
}
