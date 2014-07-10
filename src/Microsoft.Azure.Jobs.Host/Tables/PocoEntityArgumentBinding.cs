using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class PocoEntityArgumentBinding<TElement> : IArgumentBinding<TableEntityContext>
        where TElement : new()
    {
        public Type ValueType
        {
            get { return typeof(TElement); }
        }

        public IValueProvider Bind(TableEntityContext value, FunctionBindingContext context)
        {
            TableOperation retrieve = TableOperation.Retrieve<DynamicTableEntity>(value.PartitionKey, value.RowKey);
            TableResult result = value.Table.Execute(retrieve);
            DynamicTableEntity entity = (DynamicTableEntity)result.Result;

            if (entity == null)
            {
                return new NullEntityValueProvider(value, typeof(TElement));
            }

            TElement userEntity = PocoTableEntity.ToPocoEntity<TElement>(entity);

            return new PocoEntityValueBinder(value, userEntity, typeof(TElement));
        }
    }
}
