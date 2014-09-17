// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.ServiceBus.Messaging;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.ServiceBus.Bindings
{
    internal class UserTypeToBrokeredMessageConverter<TInput> : IConverter<TInput, BrokeredMessage>
    {
        public BrokeredMessage Convert(TInput input)
        {
            string text = JsonConvert.SerializeObject(input, JsonSerialization.Settings);
            byte[] bytes = StrictEncodings.Utf8.GetBytes(text);
            MemoryStream stream = new MemoryStream(bytes, writable: false);

            return new BrokeredMessage(stream)
            {
                ContentType = ContentTypes.ApplicationJson
            };
        }
    }
}
