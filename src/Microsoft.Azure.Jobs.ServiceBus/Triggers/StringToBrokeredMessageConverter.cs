using System.IO;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.ServiceBus.Messaging;

namespace Microsoft.Azure.Jobs.ServiceBus.Triggers
{
    internal class StringToBrokeredMessageConverter : IConverter<string, BrokeredMessage>
    {
        public BrokeredMessage Convert(string input)
        {
            return new BrokeredMessage(new MemoryStream(StrictEncodings.Utf8.GetBytes(input), writable: false));
        }
    }
}
