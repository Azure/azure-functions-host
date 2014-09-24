// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Triggers
{
    internal class QueueMessageValueProvider : IValueProvider
    {
        private readonly IStorageQueueMessage _message;
        private readonly object _value;
        private readonly Type _valueType;

        public QueueMessageValueProvider(IStorageQueueMessage message, object value, Type valueType)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _message = message;
            _value = value;
            _valueType = valueType;
        }

        public Type Type
        {
            get { return _valueType; }
        }

        public object GetValue()
        {
            return _value;
        }

        public string ToInvokeString()
        {
            // Potential enhancement: Base64-encoded AsBytes might replay correctly when use to create a new message.
            // return _message.TryGetAsString() ?? Convert.ToBase64String(_message.AsBytes);
            return _message.TryGetAsString();
        }
    }
}
