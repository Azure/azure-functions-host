using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            ParameterInfo parameter = context.Parameter;

            if (context.Parameter.ParameterType != typeof(CloudStorageAccount))
            {
                return null;
            }

            return new CloudStorageAccountBinding(parameter.Name, context.StorageAccount);
        }
    }
}
