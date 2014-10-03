// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage.Table
#else
namespace Microsoft.Azure.WebJobs.Host.Storage.Table
#endif
{
    internal class TypeEntityResolver<TElement> : IEntityResolver where TElement : ITableEntity, new()
    {
        public Type EntityType
        {
            get { return typeof(TElement); }
        }

        public object Resolve(string partitionKey, string rowKey, DateTimeOffset timestamp,
            IDictionary<string, EntityProperty> properties, string eTag)
        {
            TElement entity = new TElement();
            entity.PartitionKey = partitionKey;
            entity.RowKey = rowKey;
            entity.Timestamp = timestamp;
            entity.ReadEntity(properties, operationContext: null);
            entity.ETag = eTag;
            return entity;
        }
    }
}
