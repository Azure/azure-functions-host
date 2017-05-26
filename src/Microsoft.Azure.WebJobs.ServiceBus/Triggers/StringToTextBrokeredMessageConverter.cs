// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.ServiceBus;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class StringToTextBrokeredMessageConverter : IConverter<string, Message>
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Message Convert(string input)
        {
            Message message = new Message(StrictEncodings.Utf8.GetBytes(input))
            {
                ContentType = ContentTypes.TextPlain
            };
            return message;
        }
    }
}
