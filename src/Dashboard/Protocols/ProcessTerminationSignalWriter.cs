using System;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.Jobs;
using Microsoft.WindowsAzure.Jobs.Host.Protocols;

namespace Dashboard.Protocols
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
            BlobClient.WriteBlob(_account, ContainerNames.AbortHostInstanceContainerName, hostInstanceId.ToString("D"), String.Empty);
        }
    }
}
