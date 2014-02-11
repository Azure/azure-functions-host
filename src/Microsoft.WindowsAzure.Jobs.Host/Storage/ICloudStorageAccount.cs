using Microsoft.WindowsAzure.Jobs.Storage.Queues;

namespace Microsoft.WindowsAzure.Jobs.Storage
{
    internal interface ICloudStorageAccount
    {
        ICloudQueueClient CreateCloudQueueClient();
    }
}
