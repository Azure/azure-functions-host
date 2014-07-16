// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
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

        public IValueProvider Bind(CloudQueue value, FunctionBindingContext context)
        {
            return new UserTypeValueBinder(value, _valueType, context.FunctionInstanceId);
        }

        private class UserTypeValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;
            private readonly Type _valueType;
            private readonly Guid _functionInstanceId;

            public UserTypeValueBinder(CloudQueue queue, Type valueType, Guid functionInstanceId)
            {
                _queue = queue;
                _valueType = valueType;
                _functionInstanceId = functionInstanceId;
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

            public void SetValue(object value)
            {
                CloudQueueMessage message = QueueCausalityManager.EncodePayload(_functionInstanceId, value);

                _queue.AddMessageAndCreateIfNotExists(message);
            }
        }
    }
}
