// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Host.Queues.Bindings
{
    internal class UserTypeArgumentBinding : IArgumentBinding<CloudQueue>
    {
        private readonly Type _valueType;

        public UserTypeArgumentBinding(Type valueType)
        {
            _valueType = valueType;
        }

        public Type ValueType
        {
            get { return _valueType; }
        }

        public Task<IValueProvider> BindAsync(CloudQueue value, ValueBindingContext context)
        {
            IValueProvider provider = new UserTypeValueBinder(value, _valueType, context.FunctionInstanceId,
                context.MessageEnqueuedWatcher);
            return Task.FromResult(provider);
        }

        private class UserTypeValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;
            private readonly Type _valueType;
            private readonly Guid _functionInstanceId;
            private readonly IMessageEnqueuedWatcher _messageEnqueuedWatcher;

            public UserTypeValueBinder(CloudQueue queue, Type valueType, Guid functionInstanceId,
                IMessageEnqueuedWatcher messageEnqueuedWatcher)
            {
                _queue = queue;
                _valueType = valueType;
                _functionInstanceId = functionInstanceId;
                _messageEnqueuedWatcher = messageEnqueuedWatcher;
            }

            public int StepOrder
            {
                get { return BindStepOrders.Enqueue; }
            }

            public Type Type
            {
                get { return _valueType; }
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
                CloudQueueMessage message;

                if (value != null)
                {
                    JObject jobject = JToken.FromObject(value) as JObject;
                    Debug.Assert(jobject != null, "Specified value object should support JSON serialization to a JObject");
                    QueueCausalityManager.SetOwner(_functionInstanceId, jobject);
                    message = new CloudQueueMessage(jobject.ToString());
                }
                else
                {
                    JValue nullValue = new JValue(value);
                    message = new CloudQueueMessage(nullValue.ToString());
                }

                await _queue.AddMessageAndCreateIfNotExistsAsync(message, cancellationToken);

                if (_messageEnqueuedWatcher != null)
                {
                    _messageEnqueuedWatcher.Notify(_queue.Name);
                }
            }
        }
    }
}
