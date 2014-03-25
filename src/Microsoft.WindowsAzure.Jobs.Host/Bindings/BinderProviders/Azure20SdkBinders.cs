using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
            Assembly sdkAssembly = null;

            string fullName = targetType.FullName;
            switch (fullName)
            {
                case Namespace + "Table.CloudTable":
                    dynamicBinder = GetTableReference;
                    sdkAssembly = targetType.Assembly;
                    break;
            }

            if (dynamicBinder == null)
            {
                if (targetType.IsGenericType && targetType.GetGenericTypeDefinition() == typeof(IQueryable<>))
                {
                    Type itemType = GetQueryableItemType(targetType);
                    Debug.Assert(itemType != null);

                    if (ImplementsITableEntity(itemType, out sdkAssembly))
                    {
                        dynamicBinder = CreateQueryableDynamicBinder(itemType);
                    }
                    else
                    {
                        throw new InvalidOperationException("IQueryable is only supported on types that implement ITableEntity.");
                    }
                }
            }

            if (dynamicBinder != null)
            {
                return new Azure20SdkTableBinder(sdkAssembly, dynamicBinder);
            }

            return null;
        }

        internal static BindResult TryBindTableEntity(CloudTableEntityDescriptor entityDescriptor, Type targetType)
        {
            Type iTableEntityType = GetITableEntityType(targetType);

            if (iTableEntityType == null)
            {
                return null;
            }

            return new TableEntityBinder(entityDescriptor, targetType, iTableEntityType).Bind();
        }

        // Returns null rather than throwing if entityType does not implement ITableEntity.
        private static Type GetITableEntityType(Type entityType)
        {
            Debug.Assert(entityType != null);
            return entityType.GetInterface(Namespace + "Table.ITableEntity");
        }

        private static Type GetQueryableItemType(Type queryableType)
        {
            Debug.Assert(queryableType != null);
            Type[] genericArguments = queryableType.GetGenericArguments();
            Debug.Assert(genericArguments != null);
            Debug.Assert(genericArguments.Length == 1);
            var itemType = genericArguments[0];
            return itemType;
        }

        private static bool ImplementsITableEntity(Type type, out Assembly sdkAssembly)
        {
            Type iTableEntityType = GetITableEntityType(type);

            if (iTableEntityType == null)
            {
                sdkAssembly = null;
                return false;
            }

            sdkAssembly = iTableEntityType.Assembly;
            return true;
        }

        private static Func<dynamic, string, object> CreateQueryableDynamicBinder(Type itemType)
        {
            MethodInfo genericMethodInfo = typeof(Azure20SdkBinderProvider).GetMethod("CreateQueryableBinder", BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(genericMethodInfo != null);
            MethodInfo methodInfo = genericMethodInfo.MakeGenericMethod(itemType);
            Debug.Assert(methodInfo != null);
            return (Func<dynamic, string, object>)Delegate.CreateDelegate(typeof(Func<dynamic, string, object>), methodInfo);
        }

        private static object CreateQueryableBinder<T>(dynamic client, string tableName)
        {
            dynamic table = GetTableReference(client, tableName);

            if (!table.Exists())
            {
                return Enumerable.Empty<T>().AsQueryable();
            }

            return table.CreateQuery<T>();
        }

        // Get a CloudStorageAccount from the same assembly as parameter.Type, bound to the storage account in binder. 
        static object GetAccount(IBinderEx binder, ParameterInfo parameter)
        {
            Assembly sdkAssembly = parameter.ParameterType.Assembly;
            return GetAccount(binder, sdkAssembly);
        }

        static object GetAccount(IBinderEx binder, Assembly sdkAssembly)
        {
            var typeCloudStorageAccount = GetCloudStorageAccountType(sdkAssembly);
            return GetAccount(binder, typeCloudStorageAccount);
        }

        private static Type GetCloudStorageAccountType(Assembly sdkAssembly)
        {
            return sdkAssembly.GetType(Namespace + "CloudStorageAccount");
        }

        static object GetAccount(IBinderEx binder, Type typeCloudStorageAccount)
        {
            return GetAccount(binder.AccountConnectionString, typeCloudStorageAccount);
        }

        private static object GetAccount(string accountConnectionString, Assembly sdkAssembly)
        {
            Type cloudStorageAccountType = GetCloudStorageAccountType(sdkAssembly);
            return GetAccount(accountConnectionString, cloudStorageAccountType);
        }

        static object GetAccount(string accountConnectionString, Type typeCloudStorageAccount)
        {
            // call CloudStorageAccount.Parse(acs);
            var m = typeCloudStorageAccount.GetMethod("Parse", BindingFlags.Static | BindingFlags.Public);
            var res = m.Invoke(null, new object[] { accountConnectionString });
            return res;
        }

        private static Type GetTableOperationType(Assembly sdkAssembly)
        {
            Type tableOperationType = sdkAssembly.GetType(Namespace + "Table.TableOperation");
            Debug.Assert(tableOperationType != null);
            return tableOperationType;
        }

        private static object GetTableReference(dynamic client, string tableName)
        {
            return client.GetTableReference(tableName);
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
            private readonly Assembly _sdkAssembly;
            private readonly Func<dynamic, string, object> _dynamicBinder;

            public Azure20SdkTableBinder(Assembly sdkAssembly, Func<dynamic, string, object> dynamicBinder)
            {
                _sdkAssembly = sdkAssembly;
                _dynamicBinder = dynamicBinder;
            }

            public BindResult Bind(IBinderEx bindingContext, Type targetType, string tableName)
            {
                dynamic account = GetAccount(bindingContext, _sdkAssembly);
                dynamic client = account.CreateCloudTableClient();

                var result = _dynamicBinder.Invoke(client, tableName);
                return new BindResult { Result = result };
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

        private class TableEntityBinder
        {
            private readonly CloudTableEntityDescriptor _entityDescriptor;
            private readonly Type _targetType;
            private readonly Type _iTableEntityType;

            public TableEntityBinder(CloudTableEntityDescriptor entityDescriptor, Type targetType, Type iTableEntityType)
            {
                _entityDescriptor = entityDescriptor;
                _targetType = targetType;
                _iTableEntityType = iTableEntityType;
            }

            public BindResult Bind()
            {
                Assembly sdkAssembly = _iTableEntityType.Assembly;
                dynamic account = GetAccount(_entityDescriptor.AccountConnectionString, sdkAssembly);
                dynamic client = account.CreateCloudTableClient();
                dynamic table = client.GetTableReference(_entityDescriptor.TableName);
                dynamic retrieveOperation = CreateRetrieveOperation(_entityDescriptor.PartitionKey,
                    _entityDescriptor.RowKey, _targetType, sdkAssembly);
                dynamic tableResult = table.Execute(retrieveOperation);
                dynamic result = tableResult.Result;

                if (result == null)
                {
                    return new BindResult { Result = null };
                }
                else
                {
                    return new TableEntityBindResult(result, table, _iTableEntityType, sdkAssembly);
                }
            }

            private static object CreateRetrieveOperation(string partitionKey, string rowKey, Type targetType, Assembly sdkAssembly)
            {
                Type tableOperationType = GetTableOperationType(sdkAssembly);
                // Call TableOperation.Retrieve<T>(partitionKey, rowKey)
                MethodInfo[] methods = tableOperationType.GetMethods(BindingFlags.Static | BindingFlags.Public);
                MethodInfo genericMethodInfo = methods.Single(m => m.Name == "Retrieve" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                MethodInfo methodInfo = genericMethodInfo.MakeGenericMethod(new Type[] { targetType });
                return methodInfo.Invoke(null, new object[] { partitionKey, rowKey });
            }
        }

        private class TableEntityBindResult : BindResult, ISelfWatch
        {
            private readonly dynamic _table;
            private readonly Type _iTableEntityType;
            private readonly Assembly _sdkAssembly;
            private readonly MethodInfo _writeEntityMethod;
            private readonly dynamic _originalProperties;

            private string _status;

            public TableEntityBindResult(dynamic result, dynamic table, Type iTableEntityType, Assembly sdkAssembly)
            {
                Result = result;
                _table = table;
                _iTableEntityType = iTableEntityType;
                _sdkAssembly = sdkAssembly;
                _writeEntityMethod = _iTableEntityType.GetMethod("WriteEntity");
                _originalProperties = WriteEntity(result);
            }

            public override ISelfWatch Watcher
            {
                get
                {
                    return this;
                }
            }

            public string GetStatus()
            {
                return _status;
            }

            public override void OnPostAction()
            {
                if (EntityHasChanged())
                {
                    _status = "1 entity updated.";
                    dynamic insertOrReplaceOperation = CreateInsertOrReplaceOperation(Result, _sdkAssembly);
                    _table.Execute(insertOrReplaceOperation);
                }
            }

            private static object CreateInsertOrReplaceOperation(dynamic entity, Assembly sdkAssembly)
            {
                Type tableOperationType = GetTableOperationType(sdkAssembly);
                // Call TableOperation.InsertOrReplace(entity)
                MethodInfo methodInfo = tableOperationType.GetMethod("InsertOrReplace", BindingFlags.Static | BindingFlags.Public);
                return methodInfo.Invoke(null, new object[] { entity });
            }

            private bool EntityHasChanged()
            {
                dynamic newProperties = WriteEntity(Result);

                if (_originalProperties.Keys.Count != newProperties.Keys.Count)
                {
                    return true;
                }

                if (!Enumerable.SequenceEqual(_originalProperties.Keys, newProperties.Keys))
                {
                    return true;
                }

                foreach (string key in newProperties.Keys)
                {
                    dynamic originalValue = _originalProperties[key];
                    dynamic newValue = newProperties[key];

                    if (originalValue == null)
                    {
                        if (newValue != null)
                        {
                            return true;
                        }
                        else
                        {
                            continue;
                        }
                    }

                    if (!originalValue.Equals(newValue))
                    {
                        return true;
                    }
                }

                return false;
            }

            private object WriteEntity(dynamic entity)
            {
                return _writeEntityMethod.Invoke(entity, new object[] { null });
            }
        }
    }
}
