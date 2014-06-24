using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Loggers
{
    internal class WebExecutionLogger : IExecutionLogger
    {
        private readonly FunctionExecutionContext _ctx;

        public WebExecutionLogger(CloudBlobClient blobClient, HostOutputMessage hostOutputMessage)
        {
            CloudBlobContainer hostsContainer = blobClient.GetContainerReference(HostContainerNames.Hosts);
            CloudBlobDirectory outputLogDirectory = hostsContainer.GetDirectoryReference(HostDirectoryNames.OutputLogs);

            _ctx = new FunctionExecutionContext
            {
                HostOutputMessage = hostOutputMessage,
                OutputLogDispenser = new FunctionOutputLogDispenser(outputLogDirectory)
            };
        }

        public FunctionExecutionContext GetExecutionContext()
        {
            return _ctx;
        }
    }
}
