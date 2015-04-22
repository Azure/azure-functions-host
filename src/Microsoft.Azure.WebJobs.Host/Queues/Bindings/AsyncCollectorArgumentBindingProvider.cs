// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class AsyncCollectorArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<IStorageQueue> TryCreate(ParameterInfo parameter)
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
            return CreateBinding(itemType);
        }

        private static IArgumentBinding<IStorageQueue> CreateBinding(Type itemType)
        {
            MethodInfo method = typeof(AsyncCollectorArgumentBindingProvider).GetMethod("CreateBindingGeneric",
                BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Assert(method != null);
            MethodInfo genericMethod = method.MakeGenericMethod(itemType);
            Debug.Assert(genericMethod != null);
            Func<IArgumentBinding<IStorageQueue>> lambda =
                (Func<IArgumentBinding<IStorageQueue>>)Delegate.CreateDelegate(
                typeof(Func<IArgumentBinding<IStorageQueue>>), genericMethod);
            return lambda.Invoke();
        }

        private static IArgumentBinding<IStorageQueue> CreateBindingGeneric<TItem>()
        {
            return new AsyncCollectorArgumentBinding<TItem>(MessageConverterFactory.Create<TItem>());
        }

        private class AsyncCollectorArgumentBinding<TItem> : IArgumentBinding<IStorageQueue>
        {
            private readonly IMessageConverterFactory<TItem> _converterFactory;

            public AsyncCollectorArgumentBinding(IMessageConverterFactory<TItem> converterFactory)
            {
                _converterFactory = converterFactory;
            }

            public Type ValueType
            {
                get { return typeof(ICollector<TItem>); }
            }

            public Task<IValueProvider> BindAsync(IStorageQueue value, ValueBindingContext context)
            {
                IConverter<TItem, IStorageQueueMessage> converter = _converterFactory.Create(value,
                    context.FunctionInstanceId);
                IAsyncCollector<TItem> collector = new QueueAsyncCollector<TItem>(value, converter);
                IValueProvider provider = new CollectorValueProvider(value, collector, typeof(IAsyncCollector<TItem>));
                return Task.FromResult(provider);
            }
        }
    }
}
