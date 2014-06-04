using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class QueueMessageValueProvider : IValueProvider
    {
        private readonly CloudQueueMessage _message;
        private readonly object _value;
        private readonly Type _valueType;

        public QueueMessageValueProvider(CloudQueueMessage message, object value, Type valueType)
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
