using System;
using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Bindings
{
    internal class StringToCloudQueueConverter : IConverter<string, CloudQueue>
    {
        private readonly CloudQueue _defaultQueue;

        public StringToCloudQueueConverter(CloudQueue defaultQueue)
        {
            _defaultQueue = defaultQueue;
        }

        public CloudQueue Convert(string input)
        {
            // For convenience, treat an an empty string as a request for the default value.
            if (String.IsNullOrEmpty(input))
            {
                return _defaultQueue;
            }

            return _defaultQueue.ServiceClient.GetQueueReference(input);
        }
    }
}
