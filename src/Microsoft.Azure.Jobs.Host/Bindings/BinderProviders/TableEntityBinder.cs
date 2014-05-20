using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Binds an ITableEntity to [Table("T","PK","RK")]
    internal class TableEntityBinder<T> where T : ITableEntity, new()
    {
        public BindResult Bind(IBinderEx bindingContext, string tableName, string partitionKey, string rowKey)
        {
            CloudStorageAccount account = Utility.GetAccount(bindingContext.StorageConnectionString);
            CloudTableClient client = account.CreateCloudTableClient();
            CloudTable table = client.GetTableReference(tableName);
            TableOperation retrieveOperation = TableOperation.Retrieve<T>(partitionKey, rowKey);
            TableResult tableResult = table.Execute(retrieveOperation);
            T result = (T)tableResult.Result;

            if (result == null)
            {
                return new BindResult { Result = null };
            }
            else
            {
                return new TableEntityBindResult(result, table);
            }
        }

        private class TableEntityBindResult : BindResult, ISelfWatch
        {
            private readonly CloudTable _table;
            private readonly ITableEntity _result;
            private readonly IDictionary<string, EntityProperty> _originalProperties;

            private string _status;

            public TableEntityBindResult(T result, CloudTable table)
            {
                Debug.Assert(result != null);
                Result = result;
                _result = result;
                _table = table;
                _originalProperties = result.WriteEntity(null);
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
                    TableOperation insertOrReplaceOperation = TableOperation.InsertOrReplace(_result);
                    _table.Execute(insertOrReplaceOperation);
                }
            }

            private bool EntityHasChanged()
            {
                IDictionary<string, EntityProperty> newProperties = _result.WriteEntity(null);

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
                    EntityProperty originalValue = _originalProperties[key];
                    EntityProperty newValue = newProperties[key];

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
        }
    }
}
