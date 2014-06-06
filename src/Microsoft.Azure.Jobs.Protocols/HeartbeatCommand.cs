using System;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a command that signals a heartbeat from a running host instance.</summary>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class HeartbeatCommand : IHeartbeatCommand
#else
    internal class HeartbeatCommand : IHeartbeatCommand
#endif
    {
        private readonly CloudBlockBlob _blob;

        /// <summary>Initializes a new instance of the <see cref="HeartbeatCommand"/> class.</summary>
        /// <param name="account">The storage account in which to write the heartbeat.</param>
        /// <param name="containerName">The name of the container in which to write the heartbeat.</param>
        /// <param name="blobName">The name of the heartbeat blob (including the directory name, if any).</param>
        public HeartbeatCommand(CloudStorageAccount account, string containerName, string blobName)
            : this(account.CreateCloudBlobClient().GetContainerReference(containerName).GetBlockBlobReference(blobName))
        {
        }

        private HeartbeatCommand(CloudBlockBlob blob)
        {
            _blob = blob;
        }

        /// <inheritdoc />
        public void Beat()
        {
            try
            {
                _blob.UploadText(String.Empty);
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    _blob.Container.CreateIfNotExists();
                    _blob.UploadText(String.Empty);
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
