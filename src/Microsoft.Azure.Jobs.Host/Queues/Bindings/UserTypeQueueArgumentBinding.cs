using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class UserTypeQueueArgumentBinding : IArgumentBinding<CloudQueue>
    {
        private readonly Type _valueType;

        public UserTypeQueueArgumentBinding(Type valueType)
        {
            _valueType = valueType;
        }

        public Type ValueType
        {
            get { return _valueType; }
        }

        public IValueProvider Bind(CloudQueue value, ArgumentBindingContext context)
        {
            return new UserTypeValueBinder(value, context.FunctionInstanceId);
        }

        private class UserTypeValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;
            private readonly Guid _functionInstanceId;

            public UserTypeValueBinder(CloudQueue queue, Guid functionInstanceId)
            {
                _queue = queue;
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
                return _queue.Name;
            }

            public void SetValue(object value)
            {
                QueueCausalityHelper causality = new QueueCausalityHelper();
                CloudQueueMessage message = causality.EncodePayload(_functionInstanceId, value);

                _queue.AddMessage(message);
            }
        }
    }
}
