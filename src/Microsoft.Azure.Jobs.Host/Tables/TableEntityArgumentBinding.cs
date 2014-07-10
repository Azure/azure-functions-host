using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityArgumentBinding<TElement> : IArgumentBinding<TableEntityContext>
        where TElement : ITableEntity, new()
    {
        public Type ValueType
        {
            get { return typeof(TElement); }
        }

        public IValueProvider Bind(TableEntityContext value, FunctionBindingContext context)
        {
            TableOperation retrieve = TableOperation.Retrieve<TElement>(value.PartitionKey, value.RowKey);
            TableResult result = value.Table.Execute(retrieve);
            TElement entity = (TElement)result.Result;

            if (entity == null)
            {
                return new NullEntityValueProvider(value, typeof(TElement));
            }

            return new TableEntityValueBinder(value, entity, typeof(TElement));
        }
    }
}
