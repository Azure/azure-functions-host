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

            /// <summary>
            /// Creates and sends a CloudQueueMessage with content provided in specified byte array.
            /// </summary>
            /// <param name="value">byte array as retrieved from user's WebJobs method argument.</param>
            /// <param name="cancellationToken">a cancellation token</param>
            /// <remarks>As this method handles out byte array parameter it distinguishes following possible scenarios:
            /// <item>
            /// <description>
            /// the value is null - no message will be sent;
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// the value is an empty byte array - a message with empty content will be sent;
            /// </description>
            /// </item>
            /// <item>
            /// <description>
            /// the value is a non-empty byte array - a message with content from given argument will be sent.
            /// </description>
            /// </item>
            /// </remarks>
            public async Task SetValueAsync(object value, CancellationToken cancellationToken)
            {
                if (value == null)
                {
                    return;
                }

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
