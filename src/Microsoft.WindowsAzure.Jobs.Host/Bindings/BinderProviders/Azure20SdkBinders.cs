using System;
using System.Reflection;

namespace Microsoft.WindowsAzure.Jobs.Azure20SdkBinders
{
    // Providers binders for Azure 2.0 types. These are in different assemblies than 1.*
    //  2.0 is in Microsoft.WindowsAzure.Storage.dll
    //  1.7 is in Microsoft.WindowsAzure.StorageClient.dll
    internal class Azure20SdkBinderProvider : ICloudBinderProvider, ICloudBlobBinderProvider, ICloudTableBinderProvider
    {
        const string Namespace = "Microsoft.WindowsAzure.Storage.";

        // Bind to 2.0 types where no binding attribute is required. 
        ICloudBinder ICloudBinderProvider.TryGetBinder(Type targetType)
        {
            string fullName = targetType.FullName;
            switch (fullName)
            {
                case Namespace + "CloudStorageAccount":
                    return new CloudStorageAccountBinder();
                case Namespace + "Queue.CloudQueue":
                    return new CloudQueueBinder();
            }
            return null;
        }

        // Bind to BlobInput/BlobOutput
        ICloudBlobBinder ICloudBlobBinderProvider.TryGetBinder(Type targetType, bool isInput)
        {
            // convert from CloudBlobContainer, blobName --> targetType
            Func<dynamic, string, object> func = null;

            string fullName = targetType.FullName;
            switch (fullName)
            {
                case Namespace + "Blob.ICloudBlob":
                    if (isInput)
                    {
                        // Assumes it's on the server. So must be reader
                        func = (container, blobName) => container.GetBlobReferenceFromServer(blobName);
                    }
                    else
                    {
                        // Writer. Default to Block blobs.
                        func = (container, blobName) => container.GetBlockBlobReference(blobName);
                    }
                    break;
                
                case Namespace + "Blob.CloudPageBlob":                    
                    func = (container, blobName) => container.GetPageBlobReference(blobName);
                    break;
                
                case Namespace + "Blob.CloudBlockBlob":
                    func = (container, blobName) => container.GetBlockBlobReference(blobName);
                    break;
            }

            if (func != null)
            {
                return new Azure20SdkBlobBinder { _func = func };            
            }

            return null;
        }

        ICloudTableBinder ICloudTableBinderProvider.TryGetBinder(Type targetType, bool isReadOnly)
        {
            // Convert from CloudBlobClient, tableName --> targetType
            Func<dynamic, string, object> dynamicBinder = null;

            string fullName = targetType.FullName;
            switch (fullName)
            {
                case Namespace + "Table.CloudTable":
                    dynamicBinder = (client, tableName) => client.GetTableReference(tableName);
                    break;
            }

            if (dynamicBinder != null)
            {
                return new Azure20SdkTableBinder(dynamicBinder);
            }

            return null;
        }

        // Get a CloudStorageAccount from the same assembly as parameter.Type, bound to the storage account in binder. 
        static object GetAccount(IBinderEx binder, ParameterInfo parameter)
        {
            Assembly sdkAssembly = parameter.ParameterType.Assembly;
            return GetAccount(binder, sdkAssembly);
        }

        static object GetAccount(IBinderEx binder, Assembly sdkAssembly)
        {
            var typeCloudStorageAccount = sdkAssembly.GetType(Namespace + "CloudStorageAccount");

            return GetAccount(binder, typeCloudStorageAccount);
        }

        static object GetAccount(IBinderEx binder, Type typeCloudStorageAccount)
        {
            return GetAccount(binder.AccountConnectionString, typeCloudStorageAccount);
        }

        static object GetAccount(string accountConnectionString, Type typeCloudStorageAccount)
        {
            // call CloudStorageAccount.Parse(acs);
            var m = typeCloudStorageAccount.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public);
            var res = m.Invoke(null, new object[] { accountConnectionString });
            return res;
        }

        class Azure20SdkBlobBinder : ICloudBlobBinder
        {
            public Func<dynamic, string, object> _func;

            public BindResult Bind(IBinderEx binder, string containerName, string blobName, Type targetType)
            {
                dynamic account = GetAccount(binder, targetType.Assembly);
                dynamic client = account.CreateCloudBlobClient();
                dynamic container = client.GetContainerReference(containerName);
                container.CreateIfNotExists();

                var blob = _func(container, blobName);
                return new BindResult { Result = blob };
            }
        }

        class Azure20SdkTableBinder : ICloudTableBinder
        {
            private readonly Func<dynamic, string, object> _dynamicBinder;

            public Azure20SdkTableBinder(Func<dynamic, string, object> dynamicBinder)
            {
                _dynamicBinder = dynamicBinder;
            }

            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                dynamic account = GetAccount(bindingContext, targetType.Assembly);
                dynamic client = account.CreateCloudTableClient();

                var table = _dynamicBinder.Invoke(client, tableName);
                return new BindResult { Result = table };
            }
        }

        // Bind to Microsoft.WindowsAzure.Storage.CloudStorageAccount
        class CloudStorageAccountBinder : ICloudBinder
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {                
                var res = GetAccount(bindingContext, parameter);
                return new BindResult { Result = res };
            }
        }

        class CloudQueueBinder : ICloudBinder, ICloudBinderVerify
        {
            public BindResult Bind(IBinderEx bindingContext, ParameterInfo parameter)
            {
                string queueName = parameter.Name;

                dynamic account = GetAccount(bindingContext, parameter);
                var q = account.CreateCloudQueueClient().GetQueueReference(queueName);
                q.CreateIfNotExists();
                
                return new BindResult { Result = q };
            }        

            void ICloudBinderVerify.Validate(ParameterInfo parameter)
            {
                string queueName = parameter.Name;
                QueueClient.ValidateQueueName(queueName);
            }
        }
    }
}
