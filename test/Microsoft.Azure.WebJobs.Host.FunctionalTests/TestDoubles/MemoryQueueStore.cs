// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.FunctionalTests.TestDoubles
{
    internal class MemoryQueueStore
    {
        private readonly ConcurrentDictionary<string, Queue> _items = new ConcurrentDictionary<string, Queue>();

        public void AddMessage(string queueName, MutableStorageQueueMessage message)
        {
            if (!_items.ContainsKey(queueName))
            {
                throw StorageExceptionFactory.Create(404, "QueueNotFound");
            }

            _items[queueName].AddMessage(message);
        }

        public void CreateIfNotExists(string queueName)
        {
            _items.AddOrUpdate(queueName, new Queue(), (_, existing) => existing);
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

        private class Queue
        {
            private readonly ConcurrentQueue<MutableStorageQueueMessage> _visibleMessages =
                new ConcurrentQueue<MutableStorageQueueMessage>();
            private readonly ConcurrentDictionary<string, MutableStorageQueueMessage> _invisibleMessages =
                new ConcurrentDictionary<string, MutableStorageQueueMessage>();

            public void AddMessage(MutableStorageQueueMessage message)
            {
                DateTimeOffset now = DateTimeOffset.Now;
                message.Id = Guid.NewGuid().ToString();
                message.InsertionTime = now;
                message.ExpirationTime = now.AddDays(7);
                message.NextVisibleTime = now;
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
                    message.DequeueCount++;
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
