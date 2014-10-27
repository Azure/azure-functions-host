// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
#if PUBLICPROTOCOL
using Microsoft.Azure.WebJobs.Storage;
using Microsoft.Azure.WebJobs.Storage.Blob;
#else
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Threading.Tasks;
using System.Threading;
using System.Diagnostics;

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

            Debug.Assert(isContainerNotFoundException);
            await _blob.Container.CreateIfNotExistsAsync(cancellationToken);
            await _blob.UploadTextAsync(String.Empty, cancellationToken: cancellationToken);
        }
    }
}
