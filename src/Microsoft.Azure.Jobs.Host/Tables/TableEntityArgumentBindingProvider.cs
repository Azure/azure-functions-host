using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class TableEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            if (!TableClient.ImplementsITableEntity(parameterType))
            {
                return null;
            }

            TableClient.VerifyDefaultConstructor(parameterType);

            return CreateBinding(parameterType);
        }

        private static IArgumentBinding<TableEntityContext> CreateBinding(Type entityType)
        {
            Type genericType = typeof(TableEntityArgumentBinding<>).MakeGenericType(entityType);
            return (IArgumentBinding<TableEntityContext>)Activator.CreateInstance(genericType);
        }

        private class TableEntityArgumentBinding<TElement> : IArgumentBinding<TableEntityContext>
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

            private class TableEntityValueBinder : IValueBinder, IWatchable, ISelfWatch
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
                    _originalProperties = entity.WriteEntity(null);
                }

                public Type Type
                {
                    get { return _valueType; }
                }

                public ISelfWatch Watcher
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

                    if (_value.PartitionKey != _entityContext.PartitionKey || _value.RowKey != _entityContext.RowKey)
                    {
                        throw new InvalidOperationException(
                            "When binding to a table entity, the partition key and row key must not be changed.");
                    }

                    if (HasChanged)
                    {
                        _entityContext.Table.Execute(TableOperation.InsertOrReplace(_value));
                    }
                }

                public string ToInvokeString()
                {
                    return _entityContext.ToInvokeString();
                }

                public string GetStatus()
                {
                    return HasChanged ? "1 entity updated." : null;
                }

                private bool HasChanged
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
            }
        }
    }
}
