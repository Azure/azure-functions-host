using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageValueProvider : IValueProvider
    {
        private static readonly UTF8Encoding _strictUtf8Encoding =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true, throwOnInvalidBytes: true);

        private readonly object _value;
        private readonly Type _valueType;
        private readonly string _invokeString;

        public BrokeredMessageValueProvider(BrokeredMessage message, object value, Type valueType)
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
