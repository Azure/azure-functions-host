// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
#if PUBLICSTORAGE
using Microsoft.Azure.WebJobs.Storage.Queue;
using Microsoft.Azure.WebJobs.Storage.Table;
#else
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
#endif
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;
using Microsoft.WindowsAzure.Storage.Table;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
    /// <summary>Represents a cloud storage account.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class SdkCloudStorageAccount : ICloudStorageAccount
#else
    internal class SdkCloudStorageAccount : ICloudStorageAccount
#endif
    {
        private readonly CloudStorageAccount _sdkAccount;

        /// <summary>Initializes a new instance of the <see cref="SdkCloudStorageAccount"/> class.</summary>
        /// <param name="sdkAccount">The underlying SDK cloud storage account.</param>
        public SdkCloudStorageAccount(CloudStorageAccount sdkAccount)
        {
            _sdkAccount = sdkAccount;
        }

        /// <inheritdoc />
        public ICloudQueueClient CreateCloudQueueClient()
        {
            CloudQueueClient sdkClient = _sdkAccount.CreateCloudQueueClient();
            return new QueueClient(sdkClient);
        }

        /// <inheritdoc />
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

            public void AddMessage(CloudQueueMessage message)
            {
                _sdk.AddMessage(message);
            }

            public void CreateIfNotExists()
            {
                _sdk.CreateIfNotExists();
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

            public void Insert(ITableEntity entity)
            {
                if (entity == null)
                {
                    throw new ArgumentNullException("entity");
                }

                TableOperation insert = TableOperation.Insert(entity);

                try
                {
                    _sdk.Execute(insert);
                }
                catch (StorageException exception)
                {
                    if (!exception.IsNotFound())
                    {
                        throw;
                    }

                    _sdk.CreateIfNotExists();
                    _sdk.Execute(insert);
                }
            }

            public IEnumerable<TElement> Query<TElement>(int? limit, params IQueryModifier[] queryModifiers) where TElement : ITableEntity, new()
            {
                IQueryable<TElement> q = _sdk.CreateQuery<TElement>();
                foreach (var queryModifier in queryModifiers)
                {
                    q = queryModifier.Apply(q);
                }

                if (limit.HasValue)
                {
                    q = q.Take(limit.Value);
                }

                try
                {
                    return q.ToArray();
                }
                catch (StorageException queryException)
                {
                    // unless it is 404, do not recover
                    if (!queryException.IsNotFound())
                    {
                        throw;
                    }
                    else
                    {
                        return Enumerable.Empty<TElement>();
                    }
                }
            }

            public TElement GetOrInsert<TElement>(TElement entity) where TElement : ITableEntity, new()
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
                    // If the insert failed because the entity already exists, try to get instead.
                    if (exception.IsConflict())
                    {
                        IQueryable queryable = _sdk.CreateQuery<TElement>();
                        Debug.Assert(queryable != null);
                        IQueryable<TElement> query = from TElement item in queryable
                                                     where item.PartitionKey == entity.PartitionKey
                                                     && item.RowKey == entity.RowKey
                                                     select item;
                        TElement existingItem = query.FirstOrDefault();

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
        }
    }
}
