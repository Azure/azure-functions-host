// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CollectorArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;

            if (!parameterType.IsGenericType)
            {
                return null;
            }

            Type genericTypeDefinition = parameterType.GetGenericTypeDefinition();

            if (genericTypeDefinition != typeof(ICollector<>))
            {
                return null;
            }

            Type itemType = parameterType.GetGenericArguments()[0];
            IArgumentBinding<CloudQueue> itemBinding = GetItemBinding(itemType);
            return CreateCollectorArgumentBinding(itemType, itemBinding);
        }

        internal static IArgumentBinding<CloudQueue> GetItemBinding(Type itemType)
        {
            if (itemType == typeof(CloudQueueMessage))
            {
                return new CloudQueueMessageArgumentBinding();
            }
            else if (itemType == typeof(string))
            {
                return new StringArgumentBinding();
            }
            else if (itemType == typeof(byte[]))
            {
                return new ByteArrayArgumentBinding();
            }
            else
            {
                if (typeof(IEnumerable).IsAssignableFrom(itemType))
                {
                    throw new InvalidOperationException("Nested collections are not supported.");
                }

                return new UserTypeArgumentBinding(itemType);
            }
        }

        private static IArgumentBinding<CloudQueue> CreateCollectorArgumentBinding(Type itemType,
            IArgumentBinding<CloudQueue> itemBinding)
        {
            Type collectionGenericType = typeof(CollectorQueueArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<CloudQueue>)Activator.CreateInstance(collectionGenericType, itemBinding);
        }

        private class CollectorQueueArgumentBinding<TItem> : IArgumentBinding<CloudQueue>
        {
            private readonly IArgumentBinding<CloudQueue> _itemBinding;

            public CollectorQueueArgumentBinding(IArgumentBinding<CloudQueue> itemBinding)
            {
                _itemBinding = itemBinding;
            }

            public Type ValueType
            {
                get { return typeof(ICollector<TItem>); }
            }

            public async Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
            {
                IValueBinder itemBinder = (IValueBinder)await _itemBinding.BindAsync(value, context);
                ICollection<TItem> collection = new List<TItem>();
                ICollector<TItem> collector = new CollectionCollector<TItem>(collection);
                return new CollectorValueBinder<TItem>(value, collector, typeof(ICollector<TItem>), collection,
                    itemBinder);
            }
        }
    }
}
