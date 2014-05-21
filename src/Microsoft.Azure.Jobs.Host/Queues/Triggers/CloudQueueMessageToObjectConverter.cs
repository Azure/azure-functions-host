using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    class CloudQueueMessageToObjectConverter : IConverter<CloudQueueMessage, object>
    {
        public object Convert(CloudQueueMessage input)
        {
            return input.AsString;
        }
    }
}
