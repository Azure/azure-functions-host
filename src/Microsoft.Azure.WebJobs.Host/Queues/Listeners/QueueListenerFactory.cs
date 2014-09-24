// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class QueueListenerFactory : IListenerFactory
    {
        private static string poisonQueueSuffix = "-poison";

        private readonly IStorageQueue _queue;
        private readonly IStorageQueue _poisonQueue;
        private readonly ITriggeredFunctionInstanceFactory<IStorageQueueMessage> _instanceFactory;

        public QueueListenerFactory(IStorageQueue queue,
            ITriggeredFunctionInstanceFactory<IStorageQueueMessage> instanceFactory)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }
            else if (instanceFactory == null)
            {
                throw new ArgumentNullException("instanceFactory");
            }

            _queue = queue;
            _poisonQueue = CreatePoisonQueueReference(queue.ServiceClient, queue.Name);
            _instanceFactory = instanceFactory;
        }

        public Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            QueueTriggerExecutor triggerExecutor = new QueueTriggerExecutor(_instanceFactory, executor);
            IQueueConfiguration queueConfiguration = context.QueueConfiguration;
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(QueuePollingIntervals.Minimum,
                queueConfiguration.MaxPollingInterval);
            SharedQueueWatcher sharedWatcher = context.SharedListeners.GetOrCreate<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(context));
            IListener listener = new QueueListener(_queue, _poisonQueue, triggerExecutor, delayStrategy,
                sharedWatcher, queueConfiguration.BatchSize, queueConfiguration.MaxDequeueCount);
            return Task.FromResult(listener);
        }

        private static IStorageQueue CreatePoisonQueueReference(IStorageQueueClient client, string name)
        {
            Debug.Assert(client != null);

            // Only use a corresponding poison queue if:
            // 1. The poison queue name would be valid (adding "-poison" doesn't make the name too long), and
            // 2. The queue itself isn't already a poison queue.

            if (name == null || name.EndsWith(poisonQueueSuffix, StringComparison.Ordinal))
            {
                return null;
            }

            string possiblePoisonQueueName = name + poisonQueueSuffix;

            if (!QueueClient.IsValidQueueName(possiblePoisonQueueName))
            {
                return null;
            }

            return client.GetQueueReference(possiblePoisonQueueName);
        }
    }
}
