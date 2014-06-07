using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class CloudStorageAccountBindingProvider : IBindingProvider
    {
        public IBinding TryCreate(BindingProviderContext context)
        {
            if (context.Parameter.ParameterType != typeof(CloudStorageAccount))
            {
                return null;
            }

            return new CloudStorageAccountBinding(context.StorageAccount);
        }
    }
}
