using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using AzureTables;
using Microsoft.WindowsAzure.Jobs.Azure20SdkBinders;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class TableEntityParameterRuntimeBinding : ParameterRuntimeBinding
    {
        public CloudTableEntityDescriptor Entity { get; set; }

        public override BindResult Bind(IConfiguration config, IBinderEx bindingContext, ParameterInfo targetParameter)
        {
            Type targetType = targetParameter.ParameterType;

            BindResult sdk20BindResult = Azure20SdkBinderProvider.TryBindTableEntity(Entity, targetType);

            if (sdk20BindResult != null)
            {
                return sdk20BindResult;
            }

            if (targetType.GetConstructor(Type.EmptyTypes) == null)
            {
                throw new InvalidOperationException("Table entity types must implement ITableEntity or provide a default constructor.");
            }

            MethodInfo genericMethodInfo = typeof(TableEntityParameterRuntimeBinding).GetMethod("Bind",
                BindingFlags.NonPublic | BindingFlags.Static);
            MethodInfo methodInfo = genericMethodInfo.MakeGenericMethod(new Type[] { targetType });
            Func<CloudTableEntityDescriptor, BindResult> invoker = (Func<CloudTableEntityDescriptor, BindResult>)
                Delegate.CreateDelegate(typeof(Func<CloudTableEntityDescriptor, BindResult>), methodInfo);
            return invoker.Invoke(Entity);
        }

        private static BindResult Bind<T>(CloudTableEntityDescriptor entity) where T : new()
        {
            AzureTable<T> table = TableProviderTestHook.Default.Create<T>(entity.AccountConnectionString, entity.TableName);
            IAzureTableReader<T> reader = (IAzureTableReader<T>)table;
            T result = reader.Lookup(entity.PartitionKey, entity.RowKey);

            if (result == null)
            {
                return new BindResult { Result = null };
            }
            else
            {
                return new TableEntityBindResult<T>(result, entity.PartitionKey, entity.RowKey, table);
            }
        }

        public override string ConvertToInvokeString()
        {
            return String.Format(CultureInfo.InvariantCulture,
                "{0}/{1}/{2}", Entity.TableName, Entity.PartitionKey, Entity.RowKey);
        }

        private class TableEntityBindResult<T> : BindResult, ISelfWatch where T : new()
        {
            private readonly string _partitionKey;
            private readonly string _rowKey;
            private readonly AzureTable<T> _table;
            private readonly IDictionary<string, string> _originalProperties;

            private string _status;

            public TableEntityBindResult(T result, string partitionKey, string rowKey, AzureTable<T> table)
            {
                Result = result;
                _partitionKey = partitionKey;
                _rowKey = rowKey;
                _table = table;
                _originalProperties = ObjectBinderHelpers.ConvertObjectToDict(result);
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
                    _table.Write(_partitionKey, _rowKey, Result);
                    _table.Flush();
                }
            }

            private bool EntityHasChanged()
            {
                IDictionary<string, string> newProperties = ObjectBinderHelpers.ConvertObjectToDict(Result);

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
                    string originalValue = _originalProperties[key];
                    string newValue = newProperties[key];

                    if (!String.Equals(originalValue, newValue, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
