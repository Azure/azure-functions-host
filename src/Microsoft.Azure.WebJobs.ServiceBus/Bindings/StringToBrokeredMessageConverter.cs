// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class StringToBrokeredMessageConverter : IConverter<string, BrokeredMessage>
    {
        public BrokeredMessage Convert(string input)
        {
            if (input == null)
            {
                throw new InvalidOperationException("A brokered message cannot contain a null string instance.");
            }

            byte[] bytes = StrictEncodings.Utf8.GetBytes(input);
            MemoryStream stream = new MemoryStream(bytes, writable: false);

            return new BrokeredMessage(stream)
            {
                ContentType = ContentTypes.TextPlain
            };
        }
    }
}
