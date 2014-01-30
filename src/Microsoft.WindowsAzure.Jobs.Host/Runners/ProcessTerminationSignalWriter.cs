using System;

namespace Microsoft.WindowsAzure.Jobs
{
    internal class ProcessTerminationSignalWriter : IProcessTerminationSignalWriter
    {
        private readonly CloudStorageAccount _account;

        public ProcessTerminationSignalWriter(CloudStorageAccount account)
        {
            _account = account;
        }

        public void RequestTermination(Guid hostInstanceId)
        {
            BlobClient.WriteBlob(_account, EndpointNames.AbortHostInstanceContainerName, hostInstanceId.ToString("D"), String.Empty);
        }
    }
}
