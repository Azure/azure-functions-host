// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class ByteArrayArgumentBinding : IArgumentBinding<CloudQueue>
    {
        public Type ValueType
        {
            get { return typeof(byte[]); }
        }

        public Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
        {
            IValueProvider provider = new ByteArrayValueBinder(value, context.MessageEnqueuedWatcher);
            return Task.FromResult(provider);
        }

        private class ByteArrayValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;
            private readonly IMessageEnqueuedWatcher _messageEnqueuedWatcher;

            public ByteArrayValueBinder(CloudQueue queue, IMessageEnqueuedWatcher messageEnqueuedWatcher)
            {
                _queue = queue;
                _messageEnqueuedWatcher = messageEnqueuedWatcher;
            }

            public int StepOrder
            {
                get { return BindStepOrders.Enqueue; }
            }

            public Type Type
            {
                get { return typeof(byte[]); }
            }

            public object GetValue()
            {
                return null;
            }

            public string ToInvokeString()
            {
                return _queue.Name;
            }

            public async Task SetValueAsync(object value, CancellationToken cancellationToken)
            {
                byte[] bytes = (byte[])value;

                await _queue.AddMessageAndCreateIfNotExistsAsync(new CloudQueueMessage(bytes), cancellationToken);

                if (_messageEnqueuedWatcher != null)
                {
                    _messageEnqueuedWatcher.Notify(_queue.Name);
                }
            }
        }
    }
}
