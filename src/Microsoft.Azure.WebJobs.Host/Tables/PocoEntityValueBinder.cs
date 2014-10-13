// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class PocoEntityValueBinder<TElement> : IValueBinder, IWatchable, IWatcher
    {
        private static readonly PocoToTableEntityConverter<TElement> _converter =
            PocoToTableEntityConverter<TElement>.Create();

        private readonly TableEntityContext _entityContext;
        private readonly string _eTag;
        private readonly TElement _value;
        private readonly IDictionary<string, EntityProperty> _originalProperties;

        public PocoEntityValueBinder(TableEntityContext entityContext, string eTag, TElement value)
        {
            _entityContext = entityContext;
            _eTag = eTag;
            _value = value;
            _originalProperties =
                TableEntityValueBinder.DeepClone(_converter.Convert(value).WriteEntity(operationContext: null));
        }

        public Type Type
        {
            get { return typeof(TElement); }
        }

        public IWatcher Watcher
        {
            get { return this; }
        }

        public object GetValue()
        {
            return _value;
        }

        public Task SetValueAsync(object value, CancellationToken cancellationToken)
        {
            // Not ByRef, so can ignore value argument.
            ITableEntity entity = _converter.Convert(_value);

            if (!_converter.ConvertsPartitionKey)
            {
                entity.PartitionKey = _entityContext.PartitionKey;
            }

            if (!_converter.ConvertsRowKey)
            {
                entity.RowKey = _entityContext.RowKey;
            }

            if (!_converter.ConvertsETag)
            {
                entity.ETag = _eTag;
            }

            if (entity.PartitionKey != _entityContext.PartitionKey)
            {
                throw new InvalidOperationException(
                    "When binding to a table entity, the partition key must not be changed.");
            }

            if (entity.RowKey != _entityContext.RowKey)
            {
                throw new InvalidOperationException(
                    "When binding to a table entity, the row key must not be changed.");
            }

            if (HasChanges(entity))
            {
                IStorageTable table = _entityContext.Table;
                IStorageTableOperation operation = table.CreateReplaceOperation(entity);
                return table.ExecuteAsync(operation, cancellationToken);
            }

            return Task.FromResult(0);
        }

        public string ToInvokeString()
        {
            return _entityContext.ToInvokeString();
        }

        public ParameterLog GetStatus()
        {
            return HasChanged ? new TableParameterLog { EntitiesWritten = 1 } : null;
        }

        public bool HasChanged
        {
            get
            {
                return HasChanges(_converter.Convert(_value));
            }
        }

        private bool HasChanges(ITableEntity current)
        {
            return TableEntityValueBinder.HasChanges(_originalProperties, current.WriteEntity(operationContext: null));
        }
    }
}
