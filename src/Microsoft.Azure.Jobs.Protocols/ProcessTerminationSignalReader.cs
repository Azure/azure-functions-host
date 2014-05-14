using System;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a process termination request reader.</summary>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class ProcessTerminationSignalReader : IProcessTerminationSignalReader
#else
    internal class ProcessTerminationSignalReader : IProcessTerminationSignalReader
#endif
    {
        private readonly CloudBlobContainer _container;

        /// <summary>Initializes a new instance of the <see cref="ProcessTerminationSignalReader"/> class.</summary>
        /// <param name="account">The cloud storage account.</param>
        public ProcessTerminationSignalReader(CloudStorageAccount account)
            : this(account.CreateCloudBlobClient().GetContainerReference(ContainerNames.AbortHostInstanceContainerName))
        {
        }

        private ProcessTerminationSignalReader(CloudBlobContainer container)
        {
            _container = container;
        }

        /// <inheritdoc />
        public bool IsTerminationRequested(Guid hostInstanceId)
        {
            return DoesBlockBlobExist(_container, hostInstanceId.ToString("D"));
        }

        private static bool DoesBlockBlobExist(CloudBlobContainer container, string blobName)
        {
            var blob = container.GetBlockBlobReference(blobName);

            return DoesBlobExist(blob);
        }

        [DebuggerNonUserCode]
        private static bool DoesBlobExist(ICloudBlob blob)
        {
            try
            {
                // force network call to test whether it exists
                blob.FetchAttributes();
                return true;
            }
            catch (StorageException e)
            {
                if (e.RequestInformation.HttpStatusCode == 404)
                {
                    return false;
                }

                throw;
            }
        }
    }
}
