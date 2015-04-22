// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal static class MessageConverterFactory
    {
        internal static IConverter<TInput, BrokeredMessage> Create<TInput>()
        {
            if (typeof(TInput) == typeof(BrokeredMessage))
            {
                return (IConverter<TInput, BrokeredMessage>)new IdentityConverter<TInput>();
            }
            else if (typeof(TInput) == typeof(string))
            {
                return (IConverter<TInput, BrokeredMessage>)new StringToBrokeredMessageConverter();
            }
            else if (typeof(TInput) == typeof(byte[]))
            {
                return (IConverter<TInput, BrokeredMessage>)new ByteArrayToBrokeredMessageConverter();
            }
            else
            {
                if (typeof(IEnumerable).IsAssignableFrom(typeof(TInput)))
                {
                    throw new InvalidOperationException("Nested collections are not supported.");
                }

                return new UserTypeToBrokeredMessageConverter<TInput>();
            }
        }
    }
}
