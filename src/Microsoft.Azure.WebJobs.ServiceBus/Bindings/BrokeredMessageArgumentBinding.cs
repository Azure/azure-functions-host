// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class BrokeredMessageArgumentBinding : IArgumentBinding<ServiceBusEntity>
    {
        public Type ValueType
        {
            get { return typeof(BrokeredMessage); }
        }

        public Task<IValueProvider> BindAsync(ServiceBusEntity value, ValueBindingContext context)
        {
            IValueProvider provider = new MessageValueBinder(value, context.FunctionInstanceId);
            return Task.FromResult(provider);
        }

        private class MessageValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Guid _functionInstanceId;

            public MessageValueBinder(ServiceBusEntity entity, Guid functionInstanceId)
            {
                _entity = entity;
                _functionInstanceId = functionInstanceId;
            }

            public int StepOrder
            {
                get { return BindStepOrders.Enqueue; }
            }

            public Type Type
            {
                get { return typeof(BrokeredMessage); }
            }

            public object GetValue()
            {
                return null;
            }

            public string ToInvokeString()
            {
                return _entity.MessageSender.Path;
            }

            /// <summary>
            /// Sends a BrokeredMessage to the bound queue.
            /// </summary>
            /// <param name="value">BrokeredMessage instance as retrieved from user's WebJobs method argument.</param>
            /// <param name="cancellationToken">a cancellation token</param>
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
            public async Task SetValueAsync(object value, CancellationToken cancellationToken)
            {
                if (value == null)
                {
                    return;
                }

                BrokeredMessage message = (BrokeredMessage)value;

                await _entity.SendAndCreateQueueIfNotExistsAsync(message, _functionInstanceId, cancellationToken);
            }
        }
    }
}
