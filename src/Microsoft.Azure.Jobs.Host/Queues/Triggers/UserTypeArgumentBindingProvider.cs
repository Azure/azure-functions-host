// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class UserTypeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        public IArgumentBinding<CloudQueueMessage> TryCreate(ParameterInfo parameter)
        {
            // At indexing time, attempt to bind all types.
            // (Whether or not actual binding is possible depends on the message shape at runtime.)
            return new UserTypeArgumentBinding(parameter.ParameterType);
        }

        private class UserTypeArgumentBinding : IArgumentBinding<CloudQueueMessage>
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

            public Task<IValueProvider> BindAsync(CloudQueueMessage value, ValueBindingContext context)
            {
                object convertedValue;

                try
                {
                    convertedValue = JsonCustom.DeserializeObject(value.AsString, _valueType);
                }
                catch (JsonException e)
                {
                    // Easy to have the queue payload not deserialize properly. So give a useful error. 
                    string msg = String.Format(
@"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", _valueType.Name, e.Message);
                    throw new InvalidOperationException(msg);
                }

                IValueProvider provider = new QueueMessageValueProvider(value, convertedValue, _valueType);
                return Task.FromResult(provider);
            }
        }
    }
}
