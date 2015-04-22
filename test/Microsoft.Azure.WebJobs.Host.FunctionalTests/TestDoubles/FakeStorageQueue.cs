// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class FakeStorageQueue : IStorageQueue
    {
        private readonly MemoryQueueStore _store;
        private readonly string _queueName;
        private readonly IStorageQueueClient _parent;
        private readonly CloudQueue _sdkObject;

        public FakeStorageQueue(MemoryQueueStore store, string queueName, IStorageQueueClient parent)
        {
            _store = store;
            _queueName = queueName;
            _parent = parent;
            _sdkObject = new CloudQueue(new Uri("http://localhost/" + queueName));
        }

        public string Name
        {
            get { return _queueName; }
        }

        public CloudQueue SdkObject
        {
            get { return _sdkObject; }
        }

        public IStorageQueueClient ServiceClient
        {
            get { return _parent; }
        }

        public Task AddMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            if (message == null)
            {
                throw new ArgumentNullException("message");
            }

            MutableStorageQueueMessage storeMessage = message as MutableStorageQueueMessage;

            if (storeMessage == null)
            {
                storeMessage = new FakeStorageQueueMessage(message.SdkObject);
            }

            _store.AddMessage(_queueName, storeMessage);
            return Task.FromResult(0);
        }

        public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
        {
            _store.CreateIfNotExists(_queueName);
            return Task.FromResult(0);
        }

        public IStorageQueueMessage CreateMessage(byte[] content)
        {
            return new FakeStorageQueueMessage(new CloudQueueMessage(content));
        }

        public IStorageQueueMessage CreateMessage(string content)
        {
            return new FakeStorageQueueMessage(new CloudQueueMessage(content));
        }

        public Task DeleteMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
        {
            _store.DeleteMessage(_queueName, (MutableStorageQueueMessage)message);
            return Task.FromResult(0);
        }

        public Task<bool> ExistsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_store.Exists(_queueName));
        }

        public Task<IEnumerable<IStorageQueueMessage>> GetMessagesAsync(int messageCount,
            TimeSpan? visibilityTimeout, QueueRequestOptions options, OperationContext operationContext,
            CancellationToken cancellationToken)
        {
            IEnumerable<IStorageQueueMessage> messages = _store.GetMessages(_queueName, messageCount,
                visibilityTimeout ?? TimeSpan.FromSeconds(30));
            return Task.FromResult(messages);
        }

        public Task UpdateMessageAsync(IStorageQueueMessage message, TimeSpan visibilityTimeout,
            MessageUpdateFields updateFields, CancellationToken cancellationToken)
        {
            _store.UpdateMessage(_queueName, (MutableStorageQueueMessage)message, visibilityTimeout, updateFields);
            return Task.FromResult(0);
        }
    }
}
