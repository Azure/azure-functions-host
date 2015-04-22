// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        private readonly IContextGetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;

        public UserTypeArgumentBindingProvider(IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
        {
            if (messageEnqueuedWatcherGetter == null)
            {
                throw new ArgumentNullException("messageEnqueuedWatcherGetter");
            }

            _messageEnqueuedWatcherGetter = messageEnqueuedWatcherGetter;
        }

        public IArgumentBinding<IStorageQueue> TryCreate(ParameterInfo parameter)
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

        private IArgumentBinding<IStorageQueue> CreateBinding(Type itemType)
        {
            Type genericType = typeof(UserTypeArgumentBinding<>).MakeGenericType(itemType);
            return (IArgumentBinding<IStorageQueue>)Activator.CreateInstance(genericType,
                _messageEnqueuedWatcherGetter);
        }

        private class UserTypeArgumentBinding<TInput> : IArgumentBinding<IStorageQueue>
        {
            private readonly IContextGetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;

            public UserTypeArgumentBinding(IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
            {
                if (messageEnqueuedWatcherGetter == null)
                {
                    throw new ArgumentNullException("messageEnqueuedWatcherGetter");
                }

                _messageEnqueuedWatcherGetter = messageEnqueuedWatcherGetter;
            }

            public Type ValueType
            {
                get { return typeof(TInput); }
            }

            public Task<IValueProvider> BindAsync(IStorageQueue value, ValueBindingContext context)
            {
                IConverter<TInput, IStorageQueueMessage> converter =
                    new UserTypeToStorageQueueMessageConverter<TInput>(value, context.FunctionInstanceId);
                IValueProvider provider = new ConverterValueBinder<TInput>(value, converter,
                    _messageEnqueuedWatcherGetter.Value);
                return Task.FromResult(provider);
            }
        }
    }
}
