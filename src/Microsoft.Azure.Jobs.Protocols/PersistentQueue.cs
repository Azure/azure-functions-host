using System;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;

#if PUBLICPROTOCOL
namespace Microsoft.Azure.Jobs.Protocols
#else
namespace Microsoft.Azure.Jobs.Host.Protocols
#endif
{
    /// <summary>Represents a persistent queue.</summary>
    /// <typeparam name="T">The type of messages in the queue.</typeparam>
#if PUBLICPROTOCOL
    [CLSCompliant(false)]
    public class PersistentQueue<T> : IPersistentQueue<T> where T : PersistentQueueMessage
#else
    internal class PersistentQueue<T> : IPersistentQueue<T> where T : PersistentQueueMessage
#endif
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.Indented
        };

        private readonly CloudBlobContainer _blobContainer;
        private readonly CloudTable _table;

        /// <summary>Initializes a new instance of the <see cref="PersistentQueue{T}"/> class.</summary>
        /// <param name="account">The cloud storage account.</param>
        public PersistentQueue(CloudStorageAccount account)
            : this(account, ContainerNames.EventQueueContainerName, TableNames.EventQueueTableName)
        {
        }

        /// <summary>Initializes a new instance of the <see cref="PersistentQueue{T}"/> class.</summary>
        /// <param name="account">The cloud storage account.</param>
        /// <param name="containerName">The queue container name.</param>
        /// <param name="tableName">The queue table name.</param>
        public PersistentQueue(CloudStorageAccount account, string containerName, string tableName)
        {
            _blobContainer = account.CreateCloudBlobClient().GetContainerReference(containerName);
            _table = account.CreateCloudTableClient().GetTableReference(tableName);
        }

        /// <inheritdoc />
        public T Dequeue()
        {
            _table.CreateIfNotExists();

            IQueryable<PersistentQueueEntity> queryable = _table.CreateQuery<PersistentQueueEntity>();
            IQueryable<PersistentQueueEntity> query = (from PersistentQueueEntity item in queryable
                                                       where item.PartitionKey == String.Empty
                                                       && item.RowKey.CompareTo(DateTime.UtcNow.ToString("u")) <= 0
                                                       select item).Take(1);


            while (true)
            {
                PersistentQueueEntity entity = query.FirstOrDefault();

                if (entity == null)
                {
                    return null;
                }

                PersistentQueueEntity newEntity = new PersistentQueueEntity
                {
                    PartitionKey = PersistentQueueEntity.GetPartitionKey(),
                    RowKey = PersistentQueueEntity.GetRowKey(DateTime.UtcNow.AddMinutes(5), entity.MessageId),
                    MessageId = entity.MessageId,
                    OriginalTimestamp = entity.OriginalTimestamp ?? entity.Timestamp
                };

                TableBatchOperation batch = new TableBatchOperation();
                batch.Delete(entity);
                batch.Insert(newEntity);

                try
                {
                    _table.ExecuteBatch(batch);
                    string messageBody = _blobContainer.GetBlockBlobReference(newEntity.MessageId.ToString("N")).DownloadText();
                    T message = JsonConvert.DeserializeObject<T>(messageBody, _settings);
                    message.EnqueuedOn = newEntity.OriginalTimestamp.Value;
                    message.PopReceipt = newEntity.RowKey;
                    return message;
                }
                catch (StorageException exception)
                {
                    RequestResult result = exception.RequestInformation;

                    if (result != null && result.HttpStatusCode == 409)
                    {
                        // Continue another loop iteration
                    }
                    else
                    {
                        throw;
                    }
                }
            }
        }

        /// <inheritdoc />
        public void Enqueue(T message)
        {
            _blobContainer.CreateIfNotExists();
            _table.CreateIfNotExists();

            Guid messageId = Guid.NewGuid();
            CloudBlockBlob blob = _blobContainer.GetBlockBlobReference(messageId.ToString("N"));
            string messageBody = JsonConvert.SerializeObject(message, _settings);
            blob.UploadText(messageBody);

            PersistentQueueEntity entity = new PersistentQueueEntity
            {
                PartitionKey = PersistentQueueEntity.GetPartitionKey(),
                RowKey = PersistentQueueEntity.GetRowKey(DateTime.MinValue, messageId),
                MessageId = messageId
            };
            _table.Execute(TableOperation.Insert(entity));
        }

        /// <inheritdoc />
        public void Delete(T message)
        {
            PersistentQueueEntity entity = new PersistentQueueEntity
            {
                PartitionKey = PersistentQueueEntity.GetPartitionKey(),
                RowKey = message.PopReceipt
            };
            entity.ETag = "*";
            _table.Execute(TableOperation.Delete(entity));

            _blobContainer.GetBlockBlobReference(GetMessageId(message.PopReceipt).ToString("N")).Delete();
        }

        private Guid GetMessageId(string popReceipt)
        {
            int underscoreIndex = popReceipt.IndexOf('_');
            return Guid.Parse(popReceipt.Substring(underscoreIndex + 1));
        }
    }
}
