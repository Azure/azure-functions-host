using System;
using System.Reflection;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerInterfaces;
using SimpleBatch;

namespace RunnerHost
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