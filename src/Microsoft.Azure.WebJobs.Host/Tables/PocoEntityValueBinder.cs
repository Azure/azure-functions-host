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
    internal class PocoEntityValueBinder : IValueBinder, IWatchable, IWatcher
    {
        private readonly TableEntityContext _entityContext;
        private readonly string _eTag;
        private readonly object _value;
        private readonly Type _valueType;
        private readonly IDictionary<string, string> _originalProperties;

        public PocoEntityValueBinder(TableEntityContext entityContext, string eTag, object value, Type valueType)
        {
            _entityContext = entityContext;
            _eTag = eTag;
            _value = value;
            _valueType = valueType;
            _originalProperties = ObjectBinderHelpers.ConvertObjectToDict(value);
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
            ITableEntity entity = PocoTableEntity.ToTableEntity(_entityContext.PartitionKey, _entityContext.RowKey,
                _value);
            entity.ETag = _eTag;

            if (HasChanged)
            {
                return _entityContext.Table.ExecuteAsync(TableOperation.Replace(entity), cancellationToken);
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
                IDictionary<string, string> newProperties = ObjectBinderHelpers.ConvertObjectToDict(_value);

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
