using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Bindings.BinderProviders
{
    // Binds a non-ITableEntity class to [Table("T","PK","RK")]
    internal class PocoTableEntityBinder<T> where T : new()
    {
        public BindResult Bind(IBinderEx bindingContext, string tableName, string partitionKey, string rowKey)
        {
            ICloudTableClient client = TableProviderTestHook.Default.Create(bindingContext.StorageConnectionString);
            ICloudTable table = client.GetTableReference(tableName);
            DynamicTableEntity entity = table.Retrieve<DynamicTableEntity>(partitionKey, rowKey);

            if (entity == null)
            {
                return new BindResult { Result = null };
            }
            else
            {

                return new PocoTableEntityBindResult(table, entity);
            }
        }

        private class PocoTableEntityBindResult : BindResult, ISelfWatch
        {
            private readonly ICloudTable _table;
            private readonly T _result;
            private readonly DynamicTableEntity _originalEntity;
            private readonly IDictionary<string, string> _originalProperties;

            private string _status;

            public PocoTableEntityBindResult(ICloudTable table, DynamicTableEntity entity)
            {
                _table = table;
                _originalEntity = entity;
                _result = PocoTableEntity.ToPocoEntity<T>(entity);
                Result = _result;
                _originalProperties = ObjectBinderHelpers.ConvertObjectToDict(_result);
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
                    ITableEntity entity = PocoTableEntity.ToTableEntity(
                        _originalEntity.PartitionKey, _originalEntity.RowKey, _result);
                    _table.InsertOrReplace(entity);
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
