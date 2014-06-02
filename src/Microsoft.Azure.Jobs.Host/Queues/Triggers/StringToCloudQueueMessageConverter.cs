using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    internal class StringToCloudQueueMessageConverter : IConverter<string, CloudQueueMessage>
    {
        public CloudQueueMessage Convert(string input)
        {
            return new CloudQueueMessage(input);
        }
    }
}
