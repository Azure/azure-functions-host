using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Bindings
{
    internal class UserTypeArgumentBinding : IArgumentBinding<ServiceBusEntity>
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

        public IValueProvider Bind(ServiceBusEntity value, FunctionBindingContext context)
        {
            return new UserTypeValueBinder(value, _valueType, context.FunctionInstanceId);
        }

        private class UserTypeValueBinder : IOrderedValueBinder
        {
            private readonly ServiceBusEntity _entity;
            private readonly Type _valueType;
            private readonly Guid _functionInstanceId;

            public UserTypeValueBinder(ServiceBusEntity entity, Type valueType, Guid functionInstanceId)
            {
                _entity = entity;
                _valueType = valueType;
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
                return _entity.MessageSender.Path;
            }

            public void SetValue(object value)
            {
                string text = JsonCustom.SerializeObject(value);
                byte[] bytes = StrictEncodings.Utf8.GetBytes(text);

                using (MemoryStream stream = new MemoryStream(bytes, writable: false))
                using (BrokeredMessage message = new BrokeredMessage(stream))
                {
                    _entity.SendAndCreateQueueIfNotExists(message, _functionInstanceId);
                }
            }
        }
    }
}
