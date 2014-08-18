// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class UserTypeArgumentBinding : IArgumentBinding<ServiceBusEntity>
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

        public Task<IValueProvider> BindAsync(ServiceBusEntity value, ValueBindingContext context)
        {
            IValueProvider provider = new UserTypeValueBinder(value, _valueType, context.FunctionInstanceId);
            return Task.FromResult(provider);
        }

        private class UserTypeValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Type _valueType;
            private readonly Guid _functionInstanceId;

            public UserTypeValueBinder(ServiceBusEntity entity, Type valueType, Guid functionInstanceId)
            {
                _entity = entity;
                _valueType = valueType;
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
                string text = JsonCustom.SerializeObject(value);
                byte[] bytes = StrictEncodings.Utf8.GetBytes(text);

                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                using (BrokeredMessage message = new BrokeredMessage(stream))
                {
                    await _entity.SendAndCreateQueueIfNotExistsAsync(message, _functionInstanceId, cancellationToken);
                }
            }
        }
    }
}
