// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.WebJobs.ServiceBus.Triggers
{
    internal class BrokeredMessageToByteArrayConverter : IAsyncConverter<BrokeredMessage, byte[]>
    {
        public async Task<byte[]> ConvertAsync(BrokeredMessage input, CancellationToken cancellationToken)
        {
            if (input.ContentType == ContentTypes.ApplicationOctetStream ||
                input.ContentType == ContentTypes.TextPlain ||
                input.ContentType == ContentTypes.ApplicationJson)
            {
                using (MemoryStream outputStream = new MemoryStream())
                using (Stream inputStream = input.GetBody<Stream>())
                {
                    if (inputStream == null)
                    {
                        return null;
                    }

                    const int DefaultBufferSize = 4096;
                    await inputStream.CopyToAsync(outputStream, DefaultBufferSize, cancellationToken);
                    return outputStream.ToArray();
                }
            }
            else
            {
                try
                {
                    return input.GetBody<byte[]>();
                }
                catch (SerializationException exception)
                {
                    // If we fail to deserialize here, it is because the message body was serialized using something other than the default 
                    // DataContractSerializer with a binary XmlDictionaryWriter.
                    string contentType = input.ContentType ?? "null";
                    string msg = $"The BrokeredMessage with ContentType '{contentType}' failed to deserialize to a byte[] with the message: '{exception.Message}'";

                    throw new InvalidOperationException(msg, exception);
                }
            }
        }
    }
}