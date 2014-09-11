// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.WebJobs.Host.Tables
{
    internal class TableEntityValueBinder : IValueBinder, IWatchable, IWatcher
    {
        private readonly TableEntityContext _entityContext;
        private readonly ITableEntity _value;
        private readonly Type _valueType;
        private readonly IDictionary<string, EntityProperty> _originalProperties;

        public TableEntityValueBinder(TableEntityContext entityContext, ITableEntity entity, Type valueType)
        {
            _entityContext = entityContext;
            _value = entity;
            _valueType = valueType;
            _originalProperties = DeepClone(entity.WriteEntity(null));
        }

        public Type Type
        {
            get { return _valueType; }
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

            if (_value.PartitionKey != _entityContext.PartitionKey || _value.RowKey != _entityContext.RowKey)
            {
                throw new InvalidOperationException(
                    "When binding to a table entity, the partition key and row key must not be changed.");
            }

            if (HasChanged)
            {
                return _entityContext.Table.ExecuteAsync(TableOperation.Replace(_value), cancellationToken);
            }

            return Task.FromResult(0);
        }

        public string ToInvokeString()
        {
            return _entityContext.ToInvokeString();
        }

        public ParameterLog GetStatus()
        {
            return HasChanged ? new TableParameterLog { EntitiesUpdated = 1 } : null;
        }

        public bool HasChanged
        {
            get
            {
                IDictionary<string, EntityProperty> newProperties = _value.WriteEntity(null);

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

        private static IDictionary<string, EntityProperty> DeepClone(IDictionary<string, EntityProperty> value)
        {
            if (value == null)
            {
                return null;
            }

            IDictionary<string, EntityProperty> clone = new Dictionary<string, EntityProperty>();

            foreach (KeyValuePair<string, EntityProperty> item in value)
            {
                clone.Add(item.Key, EntityProperty.CreateEntityPropertyFromObject(item.Value.PropertyAsObject));
            }

            return clone;
        }
    }
}
