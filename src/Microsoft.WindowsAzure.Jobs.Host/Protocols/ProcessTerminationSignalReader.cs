using System;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.WindowsAzure.Jobs.Host.Protocols
{
    internal class ProcessTerminationSignalReader : IProcessTerminationSignalReader
    {
        private readonly CloudStorageAccount _account;

        public ProcessTerminationSignalReader(CloudStorageAccount account)
        {
            _account = account;
        }

        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return BlobClient.DoesBlobExist(_account, ContainerNames.AbortHostInstanceContainerName, hostInstanceId.ToString("D"));
        }
    }
}
