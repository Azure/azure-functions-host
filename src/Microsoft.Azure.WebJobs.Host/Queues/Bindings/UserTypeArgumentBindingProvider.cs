// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut)
            {
                return null;
            }

            Type itemType = parameter.ParameterType.GetElementType();

            if (typeof(IEnumerable).IsAssignableFrom(itemType))
            {
                throw new InvalidOperationException(
                    "Enumerable types are not supported. Use ICollector<T> or IAsyncCollector<T> instead.");
            }
            else if (typeof(object) == itemType)
            {
                throw new InvalidOperationException("Object element types are not supported.");
            }

            return CreateBinding(itemType);
        }

        private static IArgumentBinding<CloudQueue> CreateBinding(Type itemType)
        {
            Type genericType = typeof(UserTypeArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<CloudQueue>)Activator.CreateInstance(genericType);
        }

        private class UserTypeArgumentBinding<TInput> : IArgumentBinding<CloudQueue>
        {
            public Type ValueType
            {
                get { return typeof(TInput); }
            }

            public Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
            {
                IConverter<TInput, CloudQueueMessage> converter =
                    new UserTypeToCloudQueueMessageConverter<TInput>(context.FunctionInstanceId);
                IValueProvider provider = new ConverterValueBinder<TInput>(value, converter,
                    context.MessageEnqueuedWatcher);
                return Task.FromResult(provider);
            }
        }
    }
}
