using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal interface IStaticBindingProvider
    {
        ParameterStaticBinding TryBind(ParameterInfo parameter, INameResolver nameResolver);
    }
}
