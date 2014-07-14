using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.TestCommon
{
    public class NullStorageCredentialsValidator : IStorageCredentialsValidator
    {
        public void ValidateCredentials(CloudStorageAccount account)
        {
        }
    }
}
