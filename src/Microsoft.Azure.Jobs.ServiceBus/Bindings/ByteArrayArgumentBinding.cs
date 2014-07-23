// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class ByteArrayArgumentBinding : IArgumentBinding<ServiceBusEntity>
    {
        public Type ValueType
        {
            get { return typeof(byte[]); }
        }

        public Task<IValueProvider> BindAsync(ServiceBusEntity value, FunctionBindingContext context)
        {
            IValueProvider provider = new ByteArrayValueBinder(value, context.FunctionInstanceId);
            return Task.FromResult(provider);
        }

        private class ByteArrayValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Guid _functionInstanceId;

            public ByteArrayValueBinder(ServiceBusEntity entity, Guid functionInstanceId)
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
                get { return typeof(byte[]); }
            }

            public object GetValue()
            {
                return null;
            }

            public string ToInvokeString()
            {
                return _entity.MessageSender.Path;
            }

            public async Task SetValueAsync(object value, CancellationToken cancellationToken)
            {
                byte[] bytes = (byte[])value;

                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                using (BrokeredMessage message = new BrokeredMessage(stream))
                {
                    await _entity.SendAndCreateQueueIfNotExistsAsync(message, _functionInstanceId, cancellationToken);
                }
            }
        }
    }
}
