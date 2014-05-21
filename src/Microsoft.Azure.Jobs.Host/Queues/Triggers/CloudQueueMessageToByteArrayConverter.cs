using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Triggers
{
    class CloudQueueMessageToByteArrayConverter : IConverter<CloudQueueMessage, byte[]>
    {
        public byte[] Convert(CloudQueueMessage input)
        {
            return input.AsBytes;
        }
    }
}
