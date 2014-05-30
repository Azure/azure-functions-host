using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class StringToCloudQueueConverter : IConverter<string, CloudQueue>
    {
        private readonly CloudQueueClient _client;

        public StringToCloudQueueConverter(CloudQueueClient client)
        {
            _client = client;
        }

        public CloudQueue Convert(string input)
        {
            return _client.GetQueueReference(input);
        }
    }
}
