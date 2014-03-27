using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs.Azure1SdkBinders
{
    // Providers binders for Azure 1.x types. These are in different assemblies than 2+
    //  2+ is in Microsoft.WindowsAzure.Storage.dll
    //  1.x is in Microsoft.WindowsAzure.StorageClient.dll
    internal class Azure1SdkBinderProvider : ICloudBinderProvider
    {
        const string Type = "Microsoft.WindowsAzure.CloudStorageAccount";

        // Bind to 1.x types where no binding attribute is required. 
        ICloudBinder ICloudBinderProvider.TryGetBinder(Type targetType)
        {
            if (targetType.FullName == Type)
            {
                return new CloudStorageAccountBinder();
            }

            return null;
        }

        // Bind to Microsoft.WindowsAzure.CloudStorageAccount
        class CloudStorageAccountBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                Assembly sdkAssembly = parameter.ParameterType.Assembly;
                Type cloudStorageAccountType = sdkAssembly.GetType(Type);
                var res = GetAccount(bindingContext.AccountConnectionString, cloudStorageAccountType);
                return new BindResult { Result = res };
            }

            static object GetAccount(string accountConnectionString, Type typeCloudStorageAccount)
            {
                // call CloudStorageAccount.Parse(acs);
                var m = typeCloudStorageAccount.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public);
                var res = m.Invoke(null, new object[] { accountConnectionString });
                return res;
            }
        }
    }
}
