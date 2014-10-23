// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class HostMessageListenerFactory : IListenerFactory
    {
        private static readonly TimeSpan Minimum = QueuePollingIntervals.Minimum;
        private static readonly TimeSpan DefaultMaximum = QueuePollingIntervals.DefaultMaximum;

        private readonly IStorageQueue _queue;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;

        public HostMessageListenerFactory(IStorageQueue queue,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IFunctionIndexLookup functionLookup,
            IFunctionInstanceLogger functionInstanceLogger)
        {
            if (queue == null)
            {
                throw new ArgumentNullException("queue");
            }

            if (queueConfiguration == null)
            {
                throw new ArgumentNullException("queueConfiguration");
            }

            if (backgroundExceptionDispatcher == null)
            {
                throw new ArgumentNullException("backgroundExceptionDispatcher");
            }

            if (functionLookup == null)
            {
                throw new ArgumentNullException("functionLookup");
            }

            if (functionInstanceLogger == null)
            {
                throw new ArgumentNullException("functionInstanceLogger");
            }

            _queue = queue;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
        }

        public Task<IListener> CreateAsync(IFunctionExecutor executor, ListenerFactoryContext context)
        {
            ITriggerExecutor<IStorageQueueMessage> triggerExecutor = new HostMessageExecutor(executor, _functionLookup,
                _functionInstanceLogger);
            TimeSpan configuredMaximum = _queueConfiguration.MaxPollingInterval;
            // Provide an upper bound on the maximum polling interval for run/abort from dashboard.
            // Use the default maximum for host polling (1 minute) unless the configured overall maximum is even faster.
            TimeSpan maximum = configuredMaximum < DefaultMaximum ? configuredMaximum : DefaultMaximum;
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(Minimum, maximum);
            IListener listener = new QueueListener(_queue,
                poisonQueue: null,
                triggerExecutor: triggerExecutor,
                delayStrategy: delayStrategy,
                backgroundExceptionDispatcher: _backgroundExceptionDispatcher,
                sharedWatcher: null,
                batchSize: _queueConfiguration.BatchSize,
                maxDequeueCount: _queueConfiguration.MaxDequeueCount);
            return Task.FromResult(listener);
        }
    }
}
