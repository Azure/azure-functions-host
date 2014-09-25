// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Storage.Table;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests
{
    internal class FakeStorageAccount : IStorageAccount
    {
        private static readonly StorageCredentials _credentials = new StorageCredentials();

        private readonly QueuesStore _queuesStore = new QueuesStore();

        public StorageCredentials Credentials
        {
            get { return _credentials; }
        }

        public CloudStorageAccount SdkObject
        {
            get { throw new NotImplementedException(); }
        }

        public IStorageQueueClient CreateQueueClient()
        {
            return new FakeStorageQueueClient(_queuesStore, _credentials);
        }

        public IStorageTableClient CreateTableClient()
        {
            throw new NotImplementedException();
        }

        public string ToString(bool exportSecrets)
        {
            throw new NotImplementedException();
        }

        private class FakeStorageQueueClient : IStorageQueueClient
        {
            private readonly QueuesStore _store;
            private readonly StorageCredentials _credentials;

            public FakeStorageQueueClient(QueuesStore store, StorageCredentials credentials)
            {
                _store = store;
                _credentials = credentials;
            }

            public StorageCredentials Credentials
            {
                get { return _credentials; }
            }

            public IStorageQueue GetQueueReference(string queueName)
            {
                return new FakeStorageQueue(_store, queueName, this);
            }
        }

        private class FakeStorageQueue : IStorageQueue
        {
            private readonly QueuesStore _store;
            private readonly string _queueName;
            private readonly IStorageQueueClient _parent;

            public FakeStorageQueue(QueuesStore store, string queueName, IStorageQueueClient parent)
            {
                _store = store;
                _queueName = queueName;
                _parent = parent;
            }

            public string Name
            {
                get { return _queueName; }
            }

            public IStorageQueueClient ServiceClient
            {
                get { return _parent; }
            }

            public Task AddMessageAsync(IStorageQueueMessage message, CancellationToken cancellationToken)
            {
                _store.AddMessage(_queueName, (MutableStorageQueueMessage)message);
                return Task.FromResult(0);
            }

            public Task CreateIfNotExistsAsync(CancellationToken cancellationToken)
            {
                _store.CreateIfNotExists(_queueName);
                return Task.FromResult(0);
            }

            public IStorageQueueMessage CreateMessage(string content)
            {
                return new FakeStorageQueueMessage(content);
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

        private class FakeStorageQueueMessage : MutableStorageQueueMessage
        {
            private readonly string _content;

            public FakeStorageQueueMessage(string content)
            {
                _content = content;
            }

            public override byte[] AsBytes
            {
                get
                {
                    return Encoding.UTF8.GetBytes(_content);
                }
            }

            public override string AsString
            {
                get { return _content; }
            }

            public override int DequeueCount { get; set; }

            public override DateTimeOffset? ExpirationTime { get; set; }

            public override string Id { get; set; }

            public override DateTimeOffset? InsertionTime { get; set; }

            public override DateTimeOffset? NextVisibleTime { get; set; }

            public override string PopReceipt { get; set; }

            public override CloudQueueMessage SdkObject
            {
                get { throw new NotImplementedException(); }
            }
        }

        private class QueuesStore
        {
            private readonly ConcurrentDictionary<string, QueueStore> _items =
                new ConcurrentDictionary<string, QueueStore>();

            public void AddMessage(string queueName, MutableStorageQueueMessage message)
            {
                _items[queueName].AddMessage(message);
            }

            public void CreateIfNotExists(string queueName)
            {
                _items.AddOrUpdate(queueName, new QueueStore(), (_, existing) => existing);
            }

            public void DeleteMessage(string queueName, MutableStorageQueueMessage message)
            {
                _items[queueName].DeleteMessage(message);
            }

            public bool Exists(string queueName)
            {
                return _items.ContainsKey(queueName);
            }

            public IEnumerable<MutableStorageQueueMessage> GetMessages(string queueName, int messageCount,
                TimeSpan visibilityTimeout)
            {
                return _items[queueName].GetMessages(messageCount, visibilityTimeout);
            }

            public void UpdateMessage(string queueName, MutableStorageQueueMessage message, TimeSpan visibilityTimeout,
                MessageUpdateFields updateFields)
            {
                _items[queueName].UpdateMessage(message, visibilityTimeout, updateFields);
            }

            private class QueueStore
            {
                private readonly ConcurrentQueue<MutableStorageQueueMessage> _visibleMessages =
                    new ConcurrentQueue<MutableStorageQueueMessage>();
                private readonly ConcurrentDictionary<string, MutableStorageQueueMessage> _invisibleMessages =
                    new ConcurrentDictionary<string, MutableStorageQueueMessage>();

                public void AddMessage(MutableStorageQueueMessage message)
                {
                    if (message.NextVisibleTime.HasValue)
                    {
                        throw new NotSupportedException();
                    }

                    _visibleMessages.Enqueue(message);
                }

                public void DeleteMessage(MutableStorageQueueMessage message)
                {
                    MutableStorageQueueMessage ignore;

                    if (!_invisibleMessages.TryRemove(message.PopReceipt, out ignore))
                    {
                        throw new InvalidOperationException("Unable to delete message.");
                    }
                }

                public IEnumerable<MutableStorageQueueMessage> GetMessages(int messageCount, TimeSpan visibilityTimeout)
                {
                    MakeExpiredInvisibleMessagesVisible();
                    List<MutableStorageQueueMessage> messages = new List<MutableStorageQueueMessage>();
                    MutableStorageQueueMessage message;

                    for (int count = 0; count < messageCount && _visibleMessages.TryDequeue(out message); count++)
                    {
                        string popReceipt = Guid.NewGuid().ToString();
                        message.NextVisibleTime = DateTimeOffset.Now.Add(visibilityTimeout);
                        message.PopReceipt = popReceipt;
                        _invisibleMessages[popReceipt] = message;
                        messages.Add(message);
                    }

                    return messages;
                }

                public void UpdateMessage(MutableStorageQueueMessage message, TimeSpan visibilityTimeout,
                    MessageUpdateFields updateFields)
                {
                    if ((updateFields & MessageUpdateFields.Content) == MessageUpdateFields.Content)
                    {
                        // No-op; queue messages already provide in-memory content updating.
                    }

                    if ((updateFields & MessageUpdateFields.Visibility) == MessageUpdateFields.Visibility)
                    {
                        message.NextVisibleTime = DateTimeOffset.Now.Add(visibilityTimeout);
                    }
                }

                private void MakeExpiredInvisibleMessagesVisible()
                {
                    KeyValuePair<string, MutableStorageQueueMessage>[] invisibleMessagesSnapshot =
                        _invisibleMessages.ToArray();
                    IEnumerable<string> expiredInvisibleMessagePopReceipts =
                        invisibleMessagesSnapshot.Where(
                        p => p.Value.NextVisibleTime.Value.UtcDateTime < DateTimeOffset.UtcNow).Select(p => p.Key);
                    foreach (string popReceipt in expiredInvisibleMessagePopReceipts)
                    {
                        MutableStorageQueueMessage message;

                        if (_invisibleMessages.TryRemove(popReceipt, out message))
                        {
                            _visibleMessages.Enqueue(message);
                        }
                    }
                }
            }
        }
    }
}
