using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Protocols
{
    /// <summary>Represents a process termination request writer.</summary>
    public class ProcessTerminationSignalWriter : IProcessTerminationSignalWriter
    {
        private readonly CloudBlobContainer _container;

        /// <summary>Instantiates a new instance of the <see cref="ProcessTerminationSignalWriter"/> class.</summary>
        /// <param name="account">The cloud storage account</param>
        [CLSCompliant(false)]
        public ProcessTerminationSignalWriter(CloudStorageAccount account)
            : this(account.CreateCloudBlobClient().GetContainerReference(ContainerNames.AbortHostInstanceContainerName))
        {
        }

        private ProcessTerminationSignalWriter(CloudBlobContainer container)
        {
            _container = container;
        }

        /// <inheritdoc />
        public void RequestTermination(Guid hostInstanceId)
        {
            WriteBlockBlob(_container, hostInstanceId.ToString("D"), String.Empty);
        }

        private static void WriteBlockBlob(CloudBlobContainer container, string blobName, string contents)
        {
            container.CreateIfNotExists();
            var blob = container.GetBlockBlobReference(blobName);
            blob.UploadText(contents);
        }
    }
}
