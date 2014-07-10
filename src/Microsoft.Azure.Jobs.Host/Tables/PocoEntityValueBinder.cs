using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class PocoEntityValueBinder : IValueBinder, IWatchable, IWatcher
    {
        private readonly TableEntityContext _entityContext;
        private readonly object _value;
        private readonly Type _valueType;
        private readonly IDictionary<string, string> _originalProperties;

        public PocoEntityValueBinder(TableEntityContext entityContext, object value, Type valueType)
        {
            _entityContext = entityContext;
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

        public void SetValue(object value)
        {
            // Not ByRef, so can ignore value argument.
            ITableEntity entity = PocoTableEntity.ToTableEntity(_entityContext.PartitionKey, _entityContext.RowKey,
                _value);

            if (HasChanged)
            {
                _entityContext.Table.Execute(TableOperation.InsertOrReplace(entity));
            }
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
