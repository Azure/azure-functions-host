using System;
using System.Reflection;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Providers binders for Azure 1.x types. These are in different assemblies than 2+
    //  2+ is in Microsoft.WindowsAzure.Storage.dll
    //  1.x is in Microsoft.WindowsAzure.StorageClient.dll
    internal class Sdk1CloudStorageAccountBinderProvider : ICloudBinderProvider
    {
        ICloudBinder ICloudBinderProvider.TryGetBinder(Type targetType)
        {
            if (targetType.FullName == "Microsoft.WindowsAzure.CloudStorageAccount")
            {
                return new CloudStorageAccountBinder();
            }

            return null;
        }

        // Bind to Microsoft.WindowsAzure.CloudStorageAccount
        private class CloudStorageAccountBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                Type cloudStorageAccountType = parameter.ParameterType;
                var res = Parse(cloudStorageAccountType, bindingContext.AccountConnectionString);
                return new BindResult { Result = res };
            }

            private static object Parse(Type cloudStorageAccountType, string accountConnectionString)
            {
                // call CloudStorageAccount.Parse(acs);
                var m = cloudStorageAccountType.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public);
                var res = m.Invoke(null, new object[] { accountConnectionString });
                return res;
            }
        }
    }
}
