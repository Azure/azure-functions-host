using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.Host.Storage.Table
{
    internal interface ICloudTable
    {
        T GetOrInsert<T>(T entity) where T : TableServiceEntity;
    }
}
