using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class StringQueueArgumentBinding : IArgumentBinding<CloudQueue>
    {
        public Type ValueType
        {
            get { return typeof(string); }
        }

        public IValueProvider Bind(CloudQueue value, ArgumentBindingContext context)
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

                _queue.AddMessage(new CloudQueueMessage(text));
            }
        }
    }
}
