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
                inputStream.CopyTo(outputStream);
                return outputStream.ToArray();
            }
        }
    }
}
