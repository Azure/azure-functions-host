// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
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

        public IValueProvider Bind(CloudQueue value, FunctionBindingContext context)
        {
            return new StringValueBinder(value);
        }

        private class StringValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;

            public StringValueBinder(CloudQueue queue)
            {
                _queue = queue;
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

            public void SetValue(object value)
            {
                string text = (string)value;

                _queue.AddMessageAndCreateIfNotExists(new CloudQueueMessage(text));
            }
        }
    }
}
