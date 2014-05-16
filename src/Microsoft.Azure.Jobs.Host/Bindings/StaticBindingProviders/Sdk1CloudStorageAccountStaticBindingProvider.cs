using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class Sdk1CloudStorageAccountStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter, INameResolver nameResolver)
        {
            if (parameter.ParameterType.FullName == "Microsoft.WindowsAzure.CloudStorageAccount")
            {
                return new Sdk1CloudStorageAccountParameterStaticBinding { Name = parameter.Name };
            }

            return null;
        }
    }
}
