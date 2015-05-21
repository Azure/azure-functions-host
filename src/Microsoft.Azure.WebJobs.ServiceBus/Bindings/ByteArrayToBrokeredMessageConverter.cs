// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class ByteArrayToBrokeredMessageConverter : IConverter<byte[], BrokeredMessage>
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public BrokeredMessage Convert(byte[] input)
        {
            if (input == null)
            {
                throw new InvalidOperationException("A brokered message cannot contain a null byte array instance.");
            }

            MemoryStream stream = new MemoryStream(input, writable: false);

            return new BrokeredMessage(stream)
            {
                ContentType = ContentTypes.ApplicationOctetStream
            };
        }
    }
}
