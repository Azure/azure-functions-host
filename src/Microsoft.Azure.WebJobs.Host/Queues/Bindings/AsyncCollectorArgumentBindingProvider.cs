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
    internal class AsyncCollectorArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            Type parameterType = parameter.ParameterType;

            if (!parameterType.IsGenericType)
            {
                return null;
            }

            Type genericTypeDefinition = parameterType.GetGenericTypeDefinition();

            if (genericTypeDefinition != typeof(IAsyncCollector<>))
            {
                return null;
            }

            Type itemType = parameterType.GetGenericArguments()[0];
            IArgumentBinding<CloudQueue> itemBinding = CollectorArgumentBindingProvider.GetItemBinding(itemType);
            return CreateAsyncCollectorArgumentBinding(itemType, itemBinding);
        }

        private static IArgumentBinding<CloudQueue> CreateAsyncCollectorArgumentBinding(Type itemType,
            IArgumentBinding<CloudQueue> itemBinding)
        {
            Type collectionGenericType = typeof(AsyncCollectorQueueArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<CloudQueue>)Activator.CreateInstance(collectionGenericType, itemBinding);
        }

        private class AsyncCollectorQueueArgumentBinding<TItem> : IArgumentBinding<CloudQueue>
        {
            private readonly IArgumentBinding<CloudQueue> _itemBinding;

            public AsyncCollectorQueueArgumentBinding(IArgumentBinding<CloudQueue> itemBinding)
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
                IAsyncCollector<TItem> collector = new CollectionAsyncCollector<TItem>(collection);
                return new CollectorValueBinder<TItem>(value, collector, typeof(IAsyncCollector<TItem>), collection,
                    itemBinder);
            }
        }
    }
}
