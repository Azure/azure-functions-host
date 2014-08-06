// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class CollectionArgumentBindingProvider : IQueueArgumentBindingProvider
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

            Type itemType = parameterType.GetGenericArguments()[0];

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

            public async Task<IValueProvider> BindAsync(ServiceBusEntity value, ValueBindingContext context)
            {
                IValueBinder itemBinder = (IValueBinder)await _itemBinding.BindAsync(value, context);
                return new CollectionValueBinder(value, itemBinder);
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

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    // Not ByRef, so can ignore value argument.
                    foreach (T item in _value)
                    {
                        await _itemBinder.SetValueAsync(item, cancellationToken);
                    }
                }
            }
        }
    }
}
