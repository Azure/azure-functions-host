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
using Microsoft.WindowsAzure.Storage.Auth;
using System.Threading;
using System.Threading.Tasks;

#if PUBLICSTORAGE
namespace Microsoft.Azure.WebJobs.Storage
#else
namespace Microsoft.Azure.WebJobs.Host.Storage
#endif
{
    /// <summary>Represents a storage account.</summary>
#if PUBLICSTORAGE
    [CLSCompliant(false)]
    public class StorageAccount : IStorageAccount
#else
    internal class StorageAccount : IStorageAccount
#endif
    {
        private readonly CloudStorageAccount _sdkAccount;

        /// <summary>Initializes a new instance of the <see cref="StorageAccount"/> class.</summary>
        /// <param name="sdkAccount">The underlying SDK cloud storage account.</param>
        public StorageAccount(CloudStorageAccount sdkAccount)
        {
            if (sdkAccount == null)
            {
                throw new ArgumentNullException("sdkAccount");
            }

            _sdkAccount = sdkAccount;
        }

        /// <inheritdoc />
        public StorageCredentials Credentials
        {
            get { return _sdkAccount.Credentials; }
        }

        /// <inheritdoc />
        public CloudStorageAccount SdkObject
        {
            get { return _sdkAccount; }
        }

        /// <inheritdoc />
        public IStorageQueueClient CreateQueueClient()
        {
            CloudQueueClient sdkClient = _sdkAccount.CreateCloudQueueClient();
            return new StorageQueueClient(sdkClient);
        }

        /// <inheritdoc />
        public IStorageTableClient CreateTableClient()
        {
            CloudTableClient sdkClient = _sdkAccount.CreateCloudTableClient();
            return new StorageTableClient(sdkClient);
        }

        /// <inheritdoc />
        public string ToString(bool exportSecrets)
        {
            return _sdkAccount.ToString(exportSecrets: exportSecrets);
        }

        private class StorageQueueClient : IStorageQueueClient
        {
            private readonly CloudQueueClient _sdk;

            public StorageQueueClient(CloudQueueClient sdk)
            {
                _sdk = sdk;
            }

            public StorageCredentials Credentials
            {
                get { return _sdk.Credentials; }
            }

            public IStorageQueue GetQueueReference(string queueName)
            {
                CloudQueue sdkQueue = _sdk.GetQueueReference(queueName);
                return new StorageQueue(this, sdkQueue);
            }
        }

        private class StorageQueue : IStorageQueue
        {
            private readonly IStorageQueueClient _parent;
            private readonly CloudQueue _sdk;

            public StorageQueue(IStorageQueueClient parent, CloudQueue sdk)
            {
                _parent = parent;
                _sdk = sdk;
            }

            public string Name
            {
                get { return _sdk.Name; }
            }

            public IStorageQueueClient ServiceClient
            {
                get { return _parent; }
            }

            public Task AddMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
            {
                CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
                return _sdk.AddMessageAsync(sdkMessage, cancellationToken);
            }

            public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
            {
                return _sdk.CreateIfNotExistsAsync(cancellationToken);
            }

            public IStorageQueueMessage CreateMessage(string content)
            {
                return new StorageQueueMessage(new CloudQueueMessage(content));
            }

            public Task DeleteMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
            {
                CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
                return _sdk.DeleteMessageAsync(sdkMessage, cancellationToken);
            }

            public Task<bool> ExistsAsync(CancellationToken cancellationToken)
            {
                return _sdk.ExistsAsync(cancellationToken);
            }

            public Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsync(int messageCount, TimeSpan? visibilityTimeout,
                QueueRequestOptions options, OperationContext operationContext, CancellationToken cancellationToken)
            {
                Task<IEnumerable<CloudQueueMessage>> innerTask = _sdk.GetMessagesAsync(messageCount,
                    visibilityTimeout, options, operationContext, cancellationToken);
                return GetMessagesAsyncCore(innerTask);
            }

            private static async Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsyncCore(
                Task<IEnumerable<CloudQueueMessage>> innerTask)
            {
                IEnumerable<CloudQueueMessage> sdkMessages = await innerTask;
                return sdkMessages.Select<CloudQueueMessage, IStorageQueueMessage>(m => new StorageQueueMessage(m));
            }

            public Task UpdateMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout,
                MessageUpdateFields updateFields, CancellationToken cancellationToken)
            {
                CloudQueueMessage sdkMessage = ((StorageQueueMessage)message).SdkObject;
                return _sdk.UpdateMessageAsync(sdkMessage, visibilityTimeout, updateFields, cancellationToken);
            }
        }

        private class StorageTableClient : IStorageTableClient
        {
            private readonly CloudTableClient _sdk;

            public StorageTableClient(CloudTableClient sdk)
            {
                _sdk = sdk;
            }

            public IStorageTable GetTableReference(string tableName)
            {
                CloudTable sdkTable = _sdk.GetTableReference(tableName);
                return new StorageTable(sdkTable);
            }
        }

        private class StorageTable : IStorageTable
        {
            private readonly CloudTable _sdk;

            public StorageTable(CloudTable sdk)
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
        }
    }
}
