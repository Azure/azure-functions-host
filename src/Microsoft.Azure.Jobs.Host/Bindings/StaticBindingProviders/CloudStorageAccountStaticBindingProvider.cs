using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings.StaticBindings;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StaticBindingProviders
{
    internal class CloudStorageAccountStaticBindingProvider : IStaticBindingProvider
    {
        public ParameterStaticBinding TryBind(ParameterInfo parameter, INameResolver nameResolver)
        {
            if (parameter.ParameterType == typeof(CloudStorageAccount))
            {
                return new CloudStorageAccountParameterStaticBinding { Name = parameter.Name };
            }

            return null;
        }
    }
}
