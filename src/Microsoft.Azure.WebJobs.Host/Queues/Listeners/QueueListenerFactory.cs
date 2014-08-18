// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class QueueListenerFactory : IListenerFactory
    {
        private static string poisonQueueSuffix = "-poison";

        private readonly CloudQueue _queue;
        private readonly CloudQueue _poisonQueue;
        private readonly ITriggeredFunctionInstanceFactory<CloudQueueMessage> _instanceFactory;

        public QueueListenerFactory(CloudQueue queue,
            ITriggeredFunctionInstanceFactory<CloudQueueMessage> instanceFactory)
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
            SharedQueueWatcher sharedWatcher = context.SharedListeners.GetOrCreate<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(context));
            IQueueConfiguration queueConfiguration = context.QueueConfiguration;
            IAlertingRecurrentCommand command = new PollQueueCommand(_queue, _poisonQueue, triggerExecutor,
                sharedWatcher, queueConfiguration.MaxDequeueCount);
            ITaskSeriesTimer timer = RandomizedExponentialBackoffStrategy.CreateTimer(command,
                QueuePollingIntervals.Minimum, queueConfiguration.MaxPollingInterval);
            IListener listener = new TimerListener(timer);
            return Task.FromResult(listener);
        }

        private static CloudQueue CreatePoisonQueueReference(CloudQueueClient client, string name)
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
