using System;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class BrokeredMessageArgumentBinding : IArgumentBinding<ServiceBusEntity>
    {
        public Type ValueType
        {
            get { return typeof(BrokeredMessage); }
        }

        public IValueProvider Bind(ServiceBusEntity value, ArgumentBindingContext context)
        {
            return new MessageValueBinder(value, context.FunctionInstanceId);
        }

        private class MessageValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Guid _functionInstanceId;

            public MessageValueBinder(ServiceBusEntity entity, Guid functionInstanceId)
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
                get { return typeof(BrokeredMessage); }
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
                BrokeredMessage message = (BrokeredMessage)value;

                _entity.SendAndCreateQueueIfNotExists(message, _functionInstanceId);
            }
        }
    }
}
