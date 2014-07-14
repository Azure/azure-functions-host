using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    internal interface IStorageCredentialsValidator
    {
        void ValidateCredentials(CloudStorageAccount account);
    }
}
