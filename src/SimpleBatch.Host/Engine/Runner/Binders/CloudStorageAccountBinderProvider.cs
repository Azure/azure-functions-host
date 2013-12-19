using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs
{
    // Provides a binding to CloudStorageAccount
    internal class CloudStorageAccountBinderProvider: ICloudBinderProvider
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
                var acs = bindingContext.AccountConnectionString;
                CloudStorageAccount account = Utility.GetAccount(acs);
                return new BindResult
                {
                    Result = account
                };
            }
        }
    }
}
