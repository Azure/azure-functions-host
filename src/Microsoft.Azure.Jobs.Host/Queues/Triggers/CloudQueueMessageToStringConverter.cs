using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    class CloudQueueMessageToStringConverter : IConverter<CloudQueueMessage, string>
    {
        public string Convert(CloudQueueMessage input)
        {
            return input.AsString;
        }
    }
}
