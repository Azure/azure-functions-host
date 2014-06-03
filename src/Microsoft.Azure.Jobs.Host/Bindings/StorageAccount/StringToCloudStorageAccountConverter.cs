using Microsoft.Azure.Jobs.Host.Converters;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Bindings.StorageAccount
{
    internal class StringToCloudStorageAccountConverter : IConverter<string, CloudStorageAccount>
    {
        public CloudStorageAccount Convert(string input)
        {
            return CloudStorageAccount.Parse(input);
        }
    }
}
