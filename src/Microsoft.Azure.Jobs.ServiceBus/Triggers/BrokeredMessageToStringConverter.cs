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
            using (TextReader reader = new StreamReader(stream, StrictEncodings.Utf8))
            {
                return reader.ReadToEnd();
            }
        }
    }
}
