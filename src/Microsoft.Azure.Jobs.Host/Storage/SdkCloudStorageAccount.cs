using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Microsoft.Azure.Jobs.Host.Storage.Queue;
using Microsoft.Azure.Jobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

namespace Microsoft.Azure.Jobs.Host.Storage
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
                _sdk.CreateIfNotExists();
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
                CloudTable sdkTable = _sdk.GetTableReference(tableName);
                return new Table(sdkTable);
            }
        }

        private class Table : ICloudTable
        {
            private readonly CloudTable _sdk;

            public Table(CloudTable sdk)
            {
                _sdk = sdk;
            }

            public T GetOrInsert<T>(T entity) where T : ITableEntity, new()
            {
                if (entity == null)
                {
                    throw new ArgumentNullException("entity");
                }

                _sdk.CreateIfNotExists();

                try
                {
                    // First, try to insert.
                    _sdk.Execute(TableOperation.Insert(entity));
                    return entity;
                }
                catch (StorageException exception)
                {
                    RequestResult result = exception.RequestInformation;

                    // If the insert failed because the entity already exists, try to get instead.
                    if (result != null && result.HttpStatusCode == 409)
                    {
                        IQueryable queryable = _sdk.CreateQuery<T>();
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

                    throw;
                }
            }

            public void InsertEntity<T>(T entity) where T : ITableEntity
            {
                if (entity == null)
                {
                    throw new ArgumentNullException("entity");
                }

                _sdk.Execute(TableOperation.Insert(entity));
            }

            public IEnumerable<T> Query<T>(int limit, params IQueryModifier[] queryModifiers) where T : ITableEntity, new()
            {
                // avoid utterly inefficient queries
                const int maxPageSize = 50;
                if (limit <= 0 || limit > maxPageSize)
                {
                    throw new ArgumentOutOfRangeException("limit", limit, String.Format(CultureInfo.CurrentCulture,
                        "limit should be a non-zero positive integer no larger than {0} ", maxPageSize));
                }

                IQueryable<T> q = _sdk.CreateQuery<T>();
                foreach (var queryModifier in queryModifiers)
                {
                    q = queryModifier.Apply(q);
                }
                q = q.Take(limit);

                try
                {
                    return q.ToArray();
                }
                catch (StorageException queryException)
                {
                    RequestResult result = queryException.RequestInformation;

                    // unless it is 404, do not recover
                    if (result == null || result.HttpStatusCode != 404)
                    {
                        throw;
                    }
                    try
                    {
                        _sdk.Create();
                    }
                    catch (StorageException createException)
                    {
                        // a possible race condition on table creation
                        if (createException.RequestInformation.HttpStatusCode != 409)
                        {
                            throw;
                        }
                    }
                    return q.ToArray();
                }
            }
        }
    }
}
