using System;
using System.Data.Services.Client;
using System.Linq;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Table;
using Microsoft.WindowsAzure.Storage.Table.DataServices;

namespace Microsoft.Azure.Jobs.Host.Protocols
{
    internal class PersistentQueue<T> : IPersistentQueue<T> where T : PersistentQueueMessage
    {
        private readonly CloudBlobContainer _blobContainer;
        private readonly CloudTableClient _tableClient;
        private readonly CloudTable _table;
        private readonly string _tableName;

        public PersistentQueue(CloudStorageAccount account)
            : this(account, ContainerNames.EventQueueContainerName, TableNames.EventQueueTableName)
        {
        }

        public PersistentQueue(CloudStorageAccount account, string containerName, string tableName)
        {
            _blobContainer = account.CreateCloudBlobClient().GetContainerReference(containerName);
            _tableClient = account.CreateCloudTableClient();
            _table = _tableClient.GetTableReference(tableName);
            _tableName = tableName;
        }

        public T Dequeue()
        {
            _table.CreateIfNotExists();

            TableServiceContext context = _tableClient.GetTableServiceContext();
            IQueryable<PersistentQueueEntity> queryable = context.CreateQuery<PersistentQueueEntity>(_tableName);
            TableServiceQuery<PersistentQueueEntity> query = (from PersistentQueueEntity item in queryable
                                                            where item.PartitionKey == String.Empty
                                                            && item.RowKey.CompareTo(DateTime.UtcNow.ToString("u")) <= 0
                                                            select item).Take(1).AsTableServiceQuery(context);


            while (true)
            {
                PersistentQueueEntity entity = query.Execute().FirstOrDefault();

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

                context.DeleteObject(entity);
                context.AddObject(_tableName, newEntity);

                try
                {
                    context.SaveChanges(SaveChangesOptions.Batch);
                    string messageBody = _blobContainer.GetBlockBlobReference(newEntity.MessageId.ToString("N")).DownloadText();
                    T message = JsonCustom.DeserializeObject<T>(messageBody);
                    message.EnqueuedOn = newEntity.OriginalTimestamp.Value;
                    message.PopReceipt = newEntity.RowKey;
                    return message;
                }
                catch (DataServiceRequestException exception)
                {
                    DataServiceResponse response = exception.Response; 

                    if (response == null)
                    {
                        throw;
                    }

                    OperationResponse firstOperation = response.FirstOrDefault();

                    if (firstOperation == null)
                    {
                        throw;
                    }

                    if (firstOperation.StatusCode == 409)
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

        public void Enqueue(T message)
        {
            _blobContainer.CreateIfNotExists();
            _tableClient.GetTableReference(_tableName).CreateIfNotExists();

            Guid messageId = Guid.NewGuid();
            CloudBlockBlob blob = _blobContainer.GetBlockBlobReference(messageId.ToString("N"));
            string messageBody = JsonCustom.SerializeObject(message);
            blob.UploadText(messageBody);

            PersistentQueueEntity entity = new PersistentQueueEntity
            {
                PartitionKey = PersistentQueueEntity.GetPartitionKey(),
                RowKey = PersistentQueueEntity.GetRowKey(DateTime.MinValue, messageId),
                MessageId = messageId
            };
            TableServiceContext context = _tableClient.GetTableServiceContext();
            context.AddObject(_tableName, entity);
            context.SaveChanges();
        }

        public void Delete(T message)
        {
            PersistentQueueEntity entity = new PersistentQueueEntity
            {
                PartitionKey = PersistentQueueEntity.GetPartitionKey(),
                RowKey = message.PopReceipt
            };
            TableServiceContext context = _tableClient.GetTableServiceContext();
            context.AttachTo(_tableName, entity, "*");
            context.DeleteObject(entity);
            context.SaveChanges();

            _blobContainer.GetBlockBlobReference(GetMessageId(message.PopReceipt).ToString("N")).Delete();
        }

        private Guid GetMessageId(string popReceipt)
        {
            int underscoreIndex = popReceipt.IndexOf('_');
            return Guid.Parse(popReceipt.Substring(underscoreIndex + 1));
        }
    }
}
