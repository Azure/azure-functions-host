using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class ByteArrayQueueArgumentBinding : IArgumentBinding<CloudQueue>
    {
        public Type ValueType
        {
            get { return typeof(byte[]); }
        }

        public IValueProvider Bind(CloudQueue value, ArgumentBindingContext context)
        {
            return new ByteArrayValueBinder(value);
        }

        private class ByteArrayValueBinder : IOrderedValueBinder
        {
            private readonly CloudQueue _queue;

            public ByteArrayValueBinder(CloudQueue queue)
            {
                _queue = queue;
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
                byte[] bytes = (byte[])value;

                _queue.AddMessage(new CloudQueueMessage(bytes));
            }
        }
    }
}
