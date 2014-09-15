// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class HostMessageListenerFactory : IListenerFactory
    {
        private static readonly TimeSpan Minimum = QueuePollingIntervals.Minimum;
        private static readonly TimeSpan DefaultMaximum = QueuePollingIntervals.DefaultMaximum;

        private readonly CloudQueue _queue;
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;

        public HostMessageListenerFactory(CloudQueue queue, IFunctionIndexLookup functionLookup,
            IFunctionInstanceLogger functionInstanceLogger)
        {
            _queue = queue;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
        }

        public Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            ITriggerExecutor<CloudQueueMessage> triggerExecutor = new HostMessageExecutor(executor, _functionLookup,
                _functionInstanceLogger);
            IQueueConfiguration queueConfiguration = context.QueueConfiguration;
            TimeSpan configuredMaximum = queueConfiguration.MaxPollingInterval;
            // Provide an upper bound on the maximum polling interval for run/abort from dashboard.
            // Use the default maximum for host polling (1 minute) unless the configured overall maximum is even faster.
            TimeSpan maximum = configuredMaximum < DefaultMaximum ? configuredMaximum : DefaultMaximum;
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(Minimum, maximum);
            IListener listener = new QueueListener(_queue,
                poisonQueue: null,
                triggerExecutor: triggerExecutor,
                delayStrategy: delayStrategy,
                sharedWatcher: null,
                batchSize: queueConfiguration.BatchSize,
                maxDequeueCount: queueConfiguration.MaxDequeueCount);
            return Task.FromResult(listener);
        }
    }
}
