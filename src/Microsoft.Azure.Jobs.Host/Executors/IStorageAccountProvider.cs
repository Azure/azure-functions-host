using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IStorageAccountProvider
    {
        CloudStorageAccount GetAccount(string connectionStringName);
    }
}
