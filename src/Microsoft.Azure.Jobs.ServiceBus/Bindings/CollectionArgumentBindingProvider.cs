using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class CollectionArgumentBindingProvider : IServiceBusArgumentBindingProvider
    {
        public IArgumentBinding<ServiceBusEntity> TryCreate(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;

            if (!parameterType.IsGenericType)
            {
                return null;
            }

            Type genericTypeDefinition = parameterType.GetGenericTypeDefinition();

            if (genericTypeDefinition != typeof(ICollection<>))
            {
                return null;
            }

            Type itemType = genericTypeDefinition.GetGenericArguments()[0];

            IArgumentBinding<ServiceBusEntity> itemBinding;

            if (itemType == typeof(BrokeredMessage))
            {
                itemBinding = new BrokeredMessageArgumentBinding();
            }
            else if (itemType == typeof(string))
            {
                itemBinding = new StringArgumentBinding();
            }
            else if (itemType == typeof(byte[]))
            {
                itemBinding = new ByteArrayArgumentBinding();
            }
            else
            {
                if (typeof(IEnumerable).IsAssignableFrom(itemType))
                {
                    throw new InvalidOperationException("Nested collections are not supported.");
                }

                itemBinding = new UserTypeArgumentBinding(itemType);
            }

            return CreateCollectionArgumentBinding(itemType, itemBinding);
        }

        private static IArgumentBinding<ServiceBusEntity> CreateCollectionArgumentBinding(Type itemType,
            IArgumentBinding<ServiceBusEntity> itemBinding)
        {
            Type collectionGenericType = typeof(CollectionQueueArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<ServiceBusEntity>)Activator.CreateInstance(collectionGenericType, itemBinding);
        }

        private class CollectionQueueArgumentBinding<T> : IArgumentBinding<ServiceBusEntity>
        {
            private readonly IArgumentBinding<ServiceBusEntity> _itemBinding;

            public CollectionQueueArgumentBinding(IArgumentBinding<ServiceBusEntity> itemBinding)
            {
                _itemBinding = itemBinding;
            }

            public Type ValueType
            {
                get { return typeof(ICollection<T>); }
            }

            public IValueProvider Bind(ServiceBusEntity value, ArgumentBindingContext context)
            {
                return new CollectionValueBinder(value, (IValueBinder)_itemBinding.Bind(value, context));
            }

            private class CollectionValueBinder : IOrderedValueBinder
            {
                private readonly ServiceBusEntity _entity;
                private readonly IValueBinder _itemBinder;
                private readonly ICollection<T> _value = new List<T>();

                public CollectionValueBinder(ServiceBusEntity entity, IValueBinder itemBinder)
                {
                    _entity = entity;
                    _itemBinder = itemBinder;
                }

                public int StepOrder
                {
                    get { return BindStepOrders.Enqueue; }
                }

                public Type Type
                {
                    get { return typeof(ICollection<T>); }
                }

                public object GetValue()
                {
                    return _value;
                }

                public string ToInvokeString()
                {
                    return _entity.MessageSender.Path;
                }

                public void SetValue(object value)
                {
                    // Not ByRef, so can ignore value argument.
                    foreach (T item in _value)
                    {
                        _itemBinder.SetValue(item);
                    }
                }
            }
        }
    }
}
