using System;
using System.IO;
using System.Reflection;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class UserTypeArgumentBindingProvider : IQueueTriggerArgumentBindingProvider
    {
        public IArgumentBinding<BrokeredMessage> TryCreate(ParameterInfo parameter)
        {
            // At indexing time, attempt to bind all types.
            // (Whether or not actual binding is possible depends on the message shape at runtime.)
            return new UserTypeArgumentBinding(parameter.ParameterType);
        }

        private class UserTypeArgumentBinding : IArgumentBinding<BrokeredMessage>
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

            public IValueProvider Bind(BrokeredMessage value, ArgumentBindingContext context)
            {
                BrokeredMessage clone = value.Clone();
                string contents;

                using (Stream stream = value.GetBody<Stream>())
                using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                {
                    contents = reader.ReadToEnd();
                }

                object convertedValue;

                try
                {
                    convertedValue = JsonCustom.DeserializeObject(contents, _valueType);
                }
                catch (JsonException e)
                {
                    // Easy to have the queue payload not deserialize properly. So give a useful error. 
                    string msg = string.Format(
    @"Binding parameters to complex objects (such as '{0}') uses Json.NET serialization. 
1. Bind the parameter type as 'string' instead of '{0}' to get the raw values and avoid JSON deserialization, or
2. Change the queue payload to be valid json. The JSON parser failed: {1}
", _valueType.Name, e.Message);
                    throw new InvalidOperationException(msg);
                }

                return new BrokeredMessageValueProvider(clone, convertedValue, _valueType);
            }
        }
    }
}
