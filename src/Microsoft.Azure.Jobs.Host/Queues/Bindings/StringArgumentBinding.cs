// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class StringArgumentBinding : IArgumentBinding<CloudQueue>
    {
        public Type ValueType
        {
            get { return typeof(string); }
        }

        public Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
        {
            IValueProvider provider = new StringValueBinder(value, context.MessageEnqueuedWatcher);
            return Task.FromResult(provider);
        }

        private class StringValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;
            private readonly IMessageEnqueuedWatcher _messageEnqueuedWatcher;

            public StringValueBinder(CloudQueue queue, IMessageEnqueuedWatcher messageEnqueuedWatcher)
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
                get { return typeof(string); }
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
                string text = (string)value;

                await _queue.AddMessageAndCreateIfNotExistsAsync(new CloudQueueMessage(text), cancellationToken);
                _messageEnqueuedWatcher.Notify(_queue.Name);
            }
        }
    }
}
