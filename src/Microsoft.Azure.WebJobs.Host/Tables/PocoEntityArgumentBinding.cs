// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class PocoEntityArgumentBinding<TElement> : IArgumentBinding<TableEntityContext>
        where TElement : new()
    {
        public Type ValueType
        {
            get { return typeof(TElement); }
        }

        public async Task<IValueProvider> BindAsync(TableEntityContext value, ValueBindingContext context)
        {
            IStorageTable table = value.Table;
            IStorageTableOperation retrieve = table.CreateRetrieveOperation<DynamicTableEntity>(
                value.PartitionKey, value.RowKey);
            TableResult result = await table.ExecuteAsync(retrieve, context.CancellationToken);
            DynamicTableEntity entity = (DynamicTableEntity)result.Result;

            if (entity == null)
            {
                return new NullEntityValueProvider(value, typeof(TElement));
            }

            TElement userEntity = PocoTableEntity.ToPocoEntity<TElement>(entity);

            return new PocoEntityValueBinder(value, entity.ETag, userEntity, typeof(TElement));
        }
    }
}
