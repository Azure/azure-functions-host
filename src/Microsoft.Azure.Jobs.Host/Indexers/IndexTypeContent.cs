using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    internal class IndexTypeContext
    {
        public IConfiguration Config { get; set; }

        public CloudStorageAccount StorageAccount { get; set; }

        public string ServiceBusConnectionString { get; set; }
    }
}
