using System;
using System.IO;
using System.Text;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageValueProvider : IValueProvider
    {
        private readonly object _value;
        private readonly Type _valueType;
        private readonly string _invokeString;

        public BrokeredMessageValueProvider(BrokeredMessage clone, object value, Type valueType)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _value = value;
            _valueType = valueType;
            _invokeString = CreateInvokeString(clone);
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

        private static string CreateInvokeString(BrokeredMessage clonedMessage)
        {
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (Stream inputStream = clonedMessage.GetBody<Stream>())
                {
                    if (inputStream == null)
                    {
                        return null;
                    }

                    inputStream.CopyTo(outputStream);
                    byte[] bytes = outputStream.ToArray();

                    try
                    {
                        return StrictEncodings.Utf8.GetString(bytes);
                    }
                    catch (DecoderFallbackException)
                    {
                        return "byte[" + bytes.Length + "]";
                    }
                }
            }
        }
    }
}
