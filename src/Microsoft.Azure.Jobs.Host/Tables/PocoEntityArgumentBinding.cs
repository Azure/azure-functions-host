// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
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

        public async Task<IValueProvider> BindAsync(TableEntityContext value, FunctionBindingContext context)
        {
            TableOperation retrieve = TableOperation.Retrieve<DynamicTableEntity>(value.PartitionKey, value.RowKey);
            TableResult result = await value.Table.ExecuteAsync(retrieve, context.CancellationToken);
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
