// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class BrokeredMessageValueProvider : IValueProvider
    {
        private readonly object _value;
        private readonly Type _valueType;
        private readonly string _invokeString;

        private BrokeredMessageValueProvider(object value, Type valueType, string invokeString)
        {
            if (value != null && !valueType.IsAssignableFrom(value.GetType()))
            {
                throw new InvalidOperationException("value is not of the correct type.");
            }

            _value = value;
            _valueType = valueType;
            _invokeString = invokeString;
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

        public static async Task<BrokeredMessageValueProvider> CreateAsync(BrokeredMessage clone, object value,
            Type valueType, CancellationToken cancellationToken)
        {
            string invokeString = await CreateInvokeStringAsync(clone, cancellationToken);
            return new BrokeredMessageValueProvider(value, valueType, invokeString);
        }

        private static async Task<string> CreateInvokeStringAsync(BrokeredMessage clonedMessage,
            CancellationToken cancellationToken)
        {
            using (MemoryStream outputStream = new MemoryStream())
            {
                using (Stream inputStream = clonedMessage.GetBody<Stream>())
                {
                    if (inputStream == null)
                    {
                        return null;
                    }

                    const int defaultBufferSize = 4096;
                    await inputStream.CopyToAsync(outputStream, defaultBufferSize, cancellationToken);
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
