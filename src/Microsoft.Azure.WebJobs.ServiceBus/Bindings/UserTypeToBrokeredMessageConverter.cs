// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics.CodeAnalysis;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class UserTypeToBrokeredMessageConverter<TInput> : IConverter<TInput, BrokeredMessage>
    {
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public BrokeredMessage Convert(TInput input)
        {
            string text = JsonConvert.SerializeObject(input, Constants.JsonSerializerSettings);
            byte[] bytes = StrictEncodings.Utf8.GetBytes(text);
            MemoryStream stream = new MemoryStream(bytes, writable: false);

            return new BrokeredMessage(stream)
            {
                ContentType = ContentTypes.ApplicationJson
            };
        }
    }
}
