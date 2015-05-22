// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
#if PUBLICPROTOCOL
using Microsoft.Azure.WebJobs.Storage;
using Microsoft.Azure.WebJobs.Storage.Blob;
#else
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
#endif
using Microsoft.WindowsAzure.Storage;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
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
        private readonly IStorageBlockBlob _blob;

        /// <summary>Initializes a new instance of the <see cref="HeartbeatCommand"/> class.</summary>
        /// <param name="account">The storage account in which to write the heartbeat.</param>
        /// <param name="containerName">The name of the container in which to write the heartbeat.</param>
        /// <param name="blobName">The name of the heartbeat blob (including the directory name, if any).</param>
        public HeartbeatCommand(IStorageAccount account, string containerName, string blobName)
            : this(account.CreateBlobClient().GetContainerReference(containerName).GetBlockBlobReference(blobName))
        {
        }

        private HeartbeatCommand(IStorageBlockBlob blob)
        {
            _blob = blob;
        }

        /// <inheritdoc />
        public async Task BeatAsync(CancellationToken cancellationToken)
        {
            bool isContainerNotFoundException = false;

            try
            {
                await _blob.UploadTextAsync(String.Empty, cancellationToken: cancellationToken);
                return;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFoundContainerNotFound())
                {
                    isContainerNotFoundException = true;
                }
                else
                {
                    throw;
                }
            }

            Debug.Assert(isContainerNotFoundException, "Blob Container was not found");
            await _blob.Container.CreateIfNotExistsAsync(cancellationToken);
            await _blob.UploadTextAsync(String.Empty, cancellationToken: cancellationToken);
        }
    }
}
