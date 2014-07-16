// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToStringConverter : IConverter<BrokeredMessage, string>
    {
        public string Convert(BrokeredMessage input)
        {
            using (Stream stream = input.GetBody<Stream>())
            {
                if (stream == null)
                {
                    return null;
                }

                using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
