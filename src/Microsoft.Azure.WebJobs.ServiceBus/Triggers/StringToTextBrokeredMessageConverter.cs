// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class StringToTextBrokeredMessageConverter : IConverter<string, BrokeredMessage>
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public BrokeredMessage Convert(string input)
        {
            MemoryStream stream = new MemoryStream(StrictEncodings.Utf8.GetBytes(input), writable: false);
            BrokeredMessage message = new BrokeredMessage(stream);
            message.ContentType = ContentTypes.TextPlain;
            return message;
        }
    }
}
