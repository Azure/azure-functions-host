using Microsoft.WindowsAzure.Jobs.Storage.Queues;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.Storage
{
    internal class SdkCloudStorageAccount : ICloudStorageAccount
    {
        private readonly CloudStorageAccount _sdkAccount;

        public SdkCloudStorageAccount(CloudStorageAccount sdkAccount)
        {
            _sdkAccount = sdkAccount;
        }

        public ICloudQueueClient CreateCloudQueueClient()
        {
            CloudQueueClient sdkClient = _sdkAccount.CreateCloudQueueClient();
            return new QueueClient(sdkClient);
        }

        private class QueueClient : ICloudQueueClient
        {
            private readonly CloudQueueClient _sdk;

            public QueueClient(CloudQueueClient sdk)
            {
                _sdk = sdk;
            }

            public ICloudQueue GetQueueReference(string queueName)
            {
                CloudQueue sdkQueue = _sdk.GetQueueReference(queueName);
                return new Queue(sdkQueue);
            }
        }

        private class Queue : ICloudQueue
        {
            private readonly CloudQueue _sdk;

            public Queue(CloudQueue sdk)
            {
                _sdk = sdk;
            }

            public void AddMessage(ICloudQueueMessage message)
            {
                QueueMessage sdkWrapper = (QueueMessage)message;
                CloudQueueMessage sdkMessage = sdkWrapper.SdkObject;

                _sdk.AddMessage(sdkMessage);
            }

            public void CreateIfNotExists()
            {
                _sdk.CreateIfNotExist();
            }

            public ICloudQueueMessage CreateMessage(string content)
            {
                CloudQueueMessage sdkMessage = new CloudQueueMessage(content);
                return new QueueMessage(sdkMessage);
            }
        }

        private class QueueMessage : ICloudQueueMessage
        {
            private readonly CloudQueueMessage _sdk;

            public QueueMessage(CloudQueueMessage sdk)
            {
                _sdk = sdk;
            }

            public CloudQueueMessage SdkObject
            {
                get { return _sdk; }
            }
        }
    }
}
