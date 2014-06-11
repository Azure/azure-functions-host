using System;
using System.Globalization;
using Microsoft.WindowsAzure.Storage.Blob;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
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
            : this(client.GetContainerReference(ContainerNames.HostOutputContainerName))
        {
        }

        /// <summary>Initializes a new instance of the <see cref="PersistentQueueWriter{T}"/> class.</summary>
        /// <param name="container">The container into which host output messages are written.</param>
        public PersistentQueueWriter(CloudBlobContainer container)
        {
            _blobContainer = container;
        }

        /// <inheritdoc />
        public void Enqueue(T message)
        {
            _blobContainer.CreateIfNotExists();

            string blobName = GetBlobName(DateTimeOffset.UtcNow, Guid.NewGuid());
            CloudBlockBlob blob = _blobContainer.GetBlockBlobReference(blobName);
            message.AddMetadata(blob.Metadata);
            string messageBody = JsonConvert.SerializeObject(message, JsonSerialization.Settings);
            blob.UploadText(messageBody);
        }

        private static string GetBlobName(DateTimeOffset timestamp, Guid messageId)
        {
            // DateTimeOffset.MaxValue.Ticks.ToString().Length = 19
            // Subtract from DateTimeOffset.MaxValue.Ticks to have newer times sort at the top.
            return String.Format(CultureInfo.InvariantCulture, "{0:D19}_{1:N}",
                DateTimeOffset.MaxValue.Ticks - timestamp.Ticks, messageId);
        }
    }
}
