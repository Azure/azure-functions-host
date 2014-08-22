// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CollectionArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
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

            IArgumentBinding<CloudQueue> itemBinding;

            if (itemType == typeof(CloudQueueMessage))
            {
                itemBinding = new CloudQueueMessageArgumentBinding();
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

        private static IArgumentBinding<CloudQueue> CreateCollectionArgumentBinding(Type itemType,
            IArgumentBinding<CloudQueue> itemBinding)
        {
            Type collectionGenericType = typeof(CollectionQueueArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<CloudQueue>)Activator.CreateInstance(collectionGenericType, itemBinding);
        }

        private class CollectionQueueArgumentBinding<TItem> : IArgumentBinding<CloudQueue>
        {
            private readonly IArgumentBinding<CloudQueue> _itemBinding;

            public CollectionQueueArgumentBinding(IArgumentBinding<CloudQueue> itemBinding)
            {
                _itemBinding = itemBinding;
            }

            public Type ValueType
            {
                get { return typeof(ICollection<TItem>); }
            }

            public async Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
            {
                IValueBinder itemBinder = (IValueBinder)await _itemBinding.BindAsync(value, context);
                return new CollectionValueBinder(value, itemBinder);
            }

            private class CollectionValueBinder : IOrderedValueBinder
            {
                private readonly CloudQueue _queue;
                private readonly IValueBinder _itemBinder;
                private readonly ICollection<TItem> _value = new List<TItem>();

                public CollectionValueBinder(CloudQueue queue, IValueBinder itemBinder)
                {
                    _queue = queue;
                    _itemBinder = itemBinder;
                }

                public int StepOrder
                {
                    get { return BindStepOrders.Enqueue; }
                }

                public Type Type
                {
                    get { return typeof(ICollection<TItem>); }
                }

                public object GetValue()
                {
                    return _value;
                }

                public string ToInvokeString()
                {
                    return _queue.Name;
                }

                public async Task SetValueAsync(object value, CancellationToken cancellationToken)
                {
                    Debug.Assert(value == null || value == GetValue(),
                        "The value argument should be either the same instance as returned by GetValue() or null");

                    // Not ByRef, so can ignore value argument.
                    foreach (TItem item in _value)
                    {
                        await _itemBinder.SetValueAsync(item, cancellationToken);
                    }
                }
            }
        }
    }
}
