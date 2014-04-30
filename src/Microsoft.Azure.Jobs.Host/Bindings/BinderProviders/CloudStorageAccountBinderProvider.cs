using System;
using System.Reflection;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    internal class CloudStorageAccountBinderProvider : ICloudBinderProvider
    {
        public ICloudBinder TryGetBinder(Type targetType)
        {
            if (targetType == typeof(CloudStorageAccount))
            {
                return new CloudStorageAccountBinder();
            }

            return null;
        }

        class CloudStorageAccountBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                CloudStorageAccount account = Utility.GetAccount(bindingContext.AccountConnectionString);
                return new BindResult { Result = account };
            }
        }
    }
}
