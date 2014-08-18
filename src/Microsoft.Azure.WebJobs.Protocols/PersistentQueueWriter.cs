// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Globalization;
#if PUBLICPROTOCOL
using Microsoft.Azure.WebJobs.Storage;
#else
using Microsoft.Azure.WebJobs.Host.Storage;
#endif
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage;
using System.Threading;
using System.Threading.Tasks;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.WebJobs.Protocols
#else
namespace Microsoft.Azure.WebJobs.Host.Protocols
#endif
{
    /// <summary>Represents a persistent queue writer.</summary>
    /// <typeparam name="T">The type of messages in the queue.</typeparam>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class PersistentQueueWriter<T> : IPersistentQueueWriter<T> where T : PersistentQueueMessage
#else
    internal class PersistentQueueWriter<T> : IPersistentQueueWriter<T> where T : PersistentQueueMessage
#endif
    {
        private readonly CloudBlobContainer _blobContainer;

        /// <summary>Initializes a new instance of the <see cref="PersistentQueueWriter{T}"/> class.</summary>
        /// <param name="client">
        /// A blob client for the storage account into which host output messages are written.
        /// </param>
        public PersistentQueueWriter(CloudBlobClient client)
            : this(client.GetContainerReference(ContainerNames.HostOutput))
        {
        }

        /// <summary>Initializes a new instance of the <see cref="PersistentQueueWriter{T}"/> class.</summary>
        /// <param name="container">The container into which host output messages are written.</param>
        public PersistentQueueWriter(CloudBlobContainer container)
        {
            _blobContainer = container;
        }

        /// <inheritdoc />
        public async Task<string> EnqueueAsync(T message, CancellationToken cancellationToken)
        {
            _blobContainer.CreateIfNotExists();

            string blobName = BlobNames.GetConflictFreeDateTimeBasedBlobName();
            CloudBlockBlob blob = _blobContainer.GetBlockBlobReference(blobName);
            message.AddMetadata(blob.Metadata);
            string messageBody = JsonConvert.SerializeObject(message, JsonSerialization.Settings);
            await blob.UploadTextAsync(messageBody, cancellationToken);

            return blobName;
        }

        /// <inheritdoc />
        public async Task DeleteAsync(string messageId, CancellationToken cancellationToken)
        {
            try
            {
                if (String.IsNullOrEmpty(messageId))
                {
                    throw new ArgumentNullException("messageId");
                }

                CloudBlockBlob blob = _blobContainer.GetBlockBlobReference(messageId);
                await blob.DeleteAsync(cancellationToken);
            }
            catch (StorageException exception)
            {
                // Return successfully if the blob has already been deleted.
                if (exception.IsNotFoundBlobOrContainerNotFound())
                {
                    return;
                }
                else
                {
                    throw;
                }
            }
        }
    }
}
