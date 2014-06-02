using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class QueueTriggerArgumentBinding : IArgumentBinding<CloudQueueMessage>
    {
        private readonly ITypeToObjectConverter<CloudQueueMessage> _converter;
        private readonly Type _valueType;

        public QueueTriggerArgumentBinding(ITypeToObjectConverter<CloudQueueMessage> converter, Type valueType)
        {
            _converter = converter;
            _valueType = valueType;
        }

        public Type ValueType
        {
            get { return _valueType; }
        }

        public IValueProvider Bind(CloudQueueMessage value, ArgumentBindingContext context)
        {
            object converted =_converter.Convert(value);
            return new ObjectMessageValueProvider(value, converted, _valueType);
        }

        private class ObjectMessageValueProvider : IValueProvider
        {
            private readonly CloudQueueMessage _message;
            private readonly object _value;
            private readonly Type _valueType;

            public ObjectMessageValueProvider(CloudQueueMessage message, object value, Type valueType)
            {
                if (!valueType.IsAssignableFrom(value.GetType()))
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
                return _message.AsString;
            }
        }
    }
}
