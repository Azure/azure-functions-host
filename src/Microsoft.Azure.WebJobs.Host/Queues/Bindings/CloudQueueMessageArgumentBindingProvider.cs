// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class CloudQueueMessageArgumentBindingProvider : IQueueArgumentBindingProvider
    {
        private readonly IContextGetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;

        public CloudQueueMessageArgumentBindingProvider(
            IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
        {
            if (messageEnqueuedWatcherGetter == null)
            {
                throw new ArgumentNullException("messageEnqueuedWatcherGetter");
            }

            _messageEnqueuedWatcherGetter = messageEnqueuedWatcherGetter;
        }

        public IArgumentBinding<IStorageQueue> TryCreate(ParameterInfo parameter)
        {
            if (!parameter.IsOut || parameter.ParameterType != typeof(CloudQueueMessage).MakeByRefType())
            {
                return null;
            }

            return new CloudQueueMessageArgumentBinding(_messageEnqueuedWatcherGetter);
        }

        internal class CloudQueueMessageArgumentBinding : IArgumentBinding<IStorageQueue>
        {
            private readonly IContextGetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherGetter;

            public CloudQueueMessageArgumentBinding(
                IContextGetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherGetter)
            {
                if (messageEnqueuedWatcherGetter == null)
                {
                    throw new ArgumentNullException("messageEnqueuedWatcherGetter");
                }

                _messageEnqueuedWatcherGetter = messageEnqueuedWatcherGetter;
            }

            public Type ValueType
            {
                get { return typeof(CloudQueueMessage); }
            }

            /// <remarks>
            /// The out message parameter is processed as follows:
            /// <list type="bullet">
            /// <item>
            /// <description>
            /// If the value is <see langword="null"/>, no message will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value has empty content, a message with empty content will be sent.
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// If the value has non-empty content, a message with that content will be sent.
            /// </description>
            /// </item>
            /// </list>
            /// </remarks>
            public Task<IValueProvider> BindAsync(IStorageQueue value, ValueBindingContext context)
            {
                IValueProvider provider = new NonNullConverterValueBinder<CloudQueueMessage>(value,
                    new CloudQueueMessageToStorageQueueMessageConverter(), _messageEnqueuedWatcherGetter.Value);
                return Task.FromResult(provider);
            }
        }
    }
}
