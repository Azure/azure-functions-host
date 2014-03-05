using System;
using System.Collections.Generic;
using System.Data.Services.Client;
using System.Diagnostics;
using System.Linq;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.StorageClient;

namespace Microsoft.WindowsAzure.Jobs.Host.Storage
{
    internal class SdkCloudStorageAccount : ICloudStorageAccount
    {
        private readonly CloudStorageAccount _sdkAccount;

        public SdkCloudStorageAccount(CloudStorageAccount sdkAccount)
        {
            _sdkAccount = sdkAccount;
        }

        public ICloudQueueClient CreateCloudQueueClient()
        {
            CloudQueueClient sdkClient = _sdkAccount.CreateCloudQueueClient();
            return new QueueClient(sdkClient);
        }

        public ICloudTableClient CreateCloudTableClient()
        {
            CloudTableClient sdkClient = _sdkAccount.CreateCloudTableClient();
            return new TableClient(sdkClient);
        }

        private class QueueClient : ICloudQueueClient
        {
            private readonly CloudQueueClient _sdk;

            public QueueClient(CloudQueueClient sdk)
            {
                _sdk = sdk;
            }

            public ICloudQueue GetQueueReference(string queueName)
            {
                CloudQueue sdkQueue = _sdk.GetQueueReference(queueName);
                return new Queue(sdkQueue);
            }
        }

        private class Queue : ICloudQueue
        {
            private readonly CloudQueue _sdk;

            public Queue(CloudQueue sdk)
            {
                _sdk = sdk;
            }

            public void AddMessage(ICloudQueueMessage message)
            {
                QueueMessage sdkWrapper = (QueueMessage)message;
                CloudQueueMessage sdkMessage = sdkWrapper.SdkObject;

                _sdk.AddMessage(sdkMessage);
            }

            public void CreateIfNotExists()
            {
                _sdk.CreateIfNotExist();
            }

            public ICloudQueueMessage CreateMessage(string content)
            {
                CloudQueueMessage sdkMessage = new CloudQueueMessage(content);
                return new QueueMessage(sdkMessage);
            }
        }

        private class QueueMessage : ICloudQueueMessage
        {
            private readonly CloudQueueMessage _sdk;

            public QueueMessage(CloudQueueMessage sdk)
            {
                _sdk = sdk;
            }

            public CloudQueueMessage SdkObject
            {
                get { return _sdk; }
            }
        }

        private class TableClient : ICloudTableClient
        {
            private readonly CloudTableClient _sdk;

            public TableClient(CloudTableClient sdk)
            {
                _sdk = sdk;
            }

            public ICloudTable GetTableReference(string tableName)
            {
                return new Table(_sdk, tableName);
            }
        }

        private class Table : ICloudTable
        {
            private readonly CloudTableClient _sdk;
            private readonly string _tableName;

            public Table(CloudTableClient sdk, string tableName)
            {
                _sdk = sdk;
                _tableName = tableName;
            }

            public T GetOrInsert<T>(T entity) where T : TableServiceEntity
            {
                if (entity == null)
                {
                    throw new ArgumentNullException("entity");
                }

                _sdk.CreateTableIfNotExist(_tableName);

                DataServiceContext insertContext = _sdk.GetDataServiceContext();

                // First, try to insert.
                insertContext.AddObject(_tableName, entity);

                try
                {
                    insertContext.SaveChanges();
                    return entity;
                }
                catch (DataServiceRequestException exception)
                {
                    DataServiceResponse response = exception.Response;

                    if (response != null)
                    {
                        OperationResponse firstOperation = response.FirstOrDefault();

                        if (firstOperation != null)
                        {
                            // If the insert failed because the entity already exists, try to get instead.
                            if (firstOperation.StatusCode == 409)
                            {

                                DataServiceContext getContext = _sdk.GetDataServiceContext();
                                IQueryable queryable = getContext.CreateQuery<T>(_tableName);
                                Debug.Assert(queryable != null);
                                IQueryable<T> query = from T item in queryable
                                                      where item.PartitionKey == entity.PartitionKey
                                                      && item.RowKey == entity.RowKey
                                                      select item;
                                T existingItem = query.FirstOrDefault();

                                // The get can fail if the object existed at the time of insert but was deleted before
                                // the get executed. At this point, give up. We already tried to insert once, and that
                                // already failed, so just propogate the original exception.
                                if (existingItem != null)
                                {
                                    return existingItem;
                                }
                            }
                        }
                    }

                    throw;
                }
            }

            public void InsertEntity<T>(T entity) where T : TableServiceEntity
            {
                if (entity == null)
                {
                    throw new ArgumentNullException("entity");
                }

                TableServiceContext context = _sdk.GetDataServiceContext();
                _sdk.CreateTableIfNotExist(_tableName);

                context.AddObject(_tableName, entity);
                context.SaveChangesWithRetries();
            }

            public IEnumerable<T> QueryByRowKeyRange<T>(string partitionKey, string rowKeyExclusiveLowerBound, string rowKeyExclusiveUpperBound, int? limit) where T : TableServiceEntity
            {
                TableServiceContext context = _sdk.GetDataServiceContext();
                var q = context.CreateQuery<T>(_tableName)
                    .Where(e => e.PartitionKey == partitionKey &&
                                e.RowKey.CompareTo(rowKeyExclusiveLowerBound) > 0 &&
                                e.RowKey.CompareTo(rowKeyExclusiveUpperBound) < 0);
                if (limit.HasValue)
                {
                    q = q.Take(limit.Value);
                }

                return q.AsTableServiceQuery().Execute();
            }
        }
    }
}
