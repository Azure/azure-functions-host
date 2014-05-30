using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class ServiceBusTriggerArgumentBinding : IArgumentBinding<BrokeredMessage>
    {
        private readonly ITypeToObjectConverter<BrokeredMessage> _converter;
        private readonly Type _valueType;

        public ServiceBusTriggerArgumentBinding(ITypeToObjectConverter<BrokeredMessage> converter, Type valueType)
        {
            _converter = converter;
            _valueType = valueType;
        }

        public Type ValueType
        {
            get { return _valueType; }
        }

        public IValueProvider Bind(BrokeredMessage value)
        {
            BrokeredMessage clone = value.Clone();
            object converted = _converter.Convert(value);
            return new ObjectMessageValueProvider(clone, converted, _valueType);
        }

        private class ObjectMessageValueProvider : IValueProvider
        {
            private static readonly UTF8Encoding _strictUtf8Encoding =
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

            private readonly object _value;
            private readonly Type _valueType;
            private readonly string _invokeString;

            public ObjectMessageValueProvider(BrokeredMessage message, object value, Type valueType)
            {
                if (!valueType.IsAssignableFrom(value.GetType()))
                {
                    throw new InvalidOperationException("value is not of the correct type.");
                }

                _value = value;
                _valueType = valueType;
                _invokeString = CreateInvokeString(message);
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
                return _invokeString;
            }

            private static string CreateInvokeString(BrokeredMessage message)
            {
                using (MemoryStream outputStream = new MemoryStream())
                {
                    using (Stream inputStream = message.GetBody<Stream>())
                    {
                        inputStream.CopyTo(outputStream);

                        try
                        {
                            return _strictUtf8Encoding.GetString(outputStream.ToArray());
                        }
                        catch (DecoderFallbackException)
                        {
                            return "byte[" + message.Size + "]";
                        }
                    }
                }
            }
        }
    }
}
