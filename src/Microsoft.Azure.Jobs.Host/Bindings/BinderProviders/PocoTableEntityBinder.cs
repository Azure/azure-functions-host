using System;
using System.Collections.Generic;
using System.Linq;
using AzureTables;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Binds a non-ITableEntity class to [Table("T","PK","RK")]
    internal class PocoTableEntityBinder<T> where T : new()
    {
        public BindResult Bind(IBinderEx bindingContext, string tableName, string partitionKey, string rowKey)
        {
            AzureTable<T> table = TableProviderTestHook.Default.Create<T>(bindingContext.AccountConnectionString, tableName);
            IAzureTableReader<T> reader = (IAzureTableReader<T>)table;
            T result = reader.Lookup(partitionKey, rowKey);

            if (result == null)
            {
                return new BindResult { Result = null };
            }
            else
            {
                return new PocoTableEntityBindResult(result, partitionKey, rowKey, table);
            }
        }

        private class PocoTableEntityBindResult : BindResult, ISelfWatch
        {
            private readonly string _partitionKey;
            private readonly string _rowKey;
            private readonly AzureTable<T> _table;
            private readonly IDictionary<string, string> _originalProperties;

            private string _status;

            public PocoTableEntityBindResult(T result, string partitionKey, string rowKey, AzureTable<T> table)
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
