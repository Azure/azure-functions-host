using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class StringArgumentBinding : IArgumentBinding<ServiceBusEntity>
    {
        public Type ValueType
        {
            get { return typeof(string); }
        }

        public IValueProvider Bind(ServiceBusEntity value, ArgumentBindingContext context)
        {
            return new StringValueBinder(value, context.FunctionInstanceId);
        }

        private class StringValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Guid _functionInstanceId;

            public StringValueBinder(ServiceBusEntity entity, Guid functionInstanceId)
            {
                _entity = entity;
                _functionInstanceId = functionInstanceId;
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
                return _entity.MessageSender.Path;
            }

            public void SetValue(object value)
            {
                string text = (string)value;
                byte[] bytes = Encoding.UTF8.GetBytes(text);

                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                using (BrokeredMessage message = new BrokeredMessage(stream))
                {
                    _entity.SendAndCreateQueueIfNotExists(message, _functionInstanceId);
                }
            }
        }
    }
}
