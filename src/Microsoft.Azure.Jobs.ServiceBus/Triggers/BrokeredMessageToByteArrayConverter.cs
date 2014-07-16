// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToByteArrayConverter : IConverter<BrokeredMessage, byte[]>
    {
        public byte[] Convert(BrokeredMessage input)
        {
            using (MemoryStream outputStream = new MemoryStream())
            using (Stream inputStream = input.GetBody<Stream>())
            {
                if (inputStream == null)
                {
                    return null;
                }

                inputStream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
        }
    }
}
