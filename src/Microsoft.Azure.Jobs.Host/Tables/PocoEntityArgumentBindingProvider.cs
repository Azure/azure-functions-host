using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Tables
{
    internal class PocoEntityArgumentBindingProvider : ITableEntityArgumentBindingProvider
    {
        public IArgumentBinding<TableEntityContext> TryCreate(Type parameterType)
        {
            if (parameterType.IsByRef)
            {
                return null;
            }

            TableClient.VerifyDefaultConstructor(parameterType);

            return CreateBinding(parameterType);
        }

        private static IArgumentBinding<TableEntityContext> CreateBinding(Type entityType)
        {
            Type genericType = typeof(PocoEntityArgumentBinding<>).MakeGenericType(entityType);
            return (IArgumentBinding<TableEntityContext>)Activator.CreateInstance(genericType);
        }

        private class PocoEntityArgumentBinding<TElement> : IArgumentBinding<TableEntityContext>
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

            private class PocoEntityValueBinder : IValueBinder, IWatchable, ISelfWatch
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
                    ITableEntity entity = PocoTableEntity.ToTableEntity(_entityContext.PartitionKey, _entityContext.RowKey, _value);

                    if (HasChanged)
                    {
                        _entityContext.Table.Execute(TableOperation.InsertOrReplace(entity));
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
    }
}
