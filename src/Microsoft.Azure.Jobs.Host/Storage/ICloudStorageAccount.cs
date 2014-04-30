using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.Azure.Jobs.Host.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Storage
{
    internal interface ICloudStorageAccount
    {
        ICloudQueueClient CreateCloudQueueClient();

        ICloudTableClient CreateCloudTableClient();
    }
}
