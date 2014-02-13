using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;

namespace Microsoft.WindowsAzure.Jobs.Host.Storage
{
    internal interface ICloudStorageAccount
    {
        ICloudQueueClient CreateCloudQueueClient();

        ICloudTableClient CreateCloudTableClient();
    }
}
