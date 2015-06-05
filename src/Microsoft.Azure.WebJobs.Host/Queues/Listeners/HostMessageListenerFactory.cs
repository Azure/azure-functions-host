// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Indexers;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class HostMessageListenerFactory : IListenerFactory
    {
        private static readonly TimeSpan Minimum = QueuePollingIntervals.Minimum;
        private static readonly TimeSpan DefaultMaximum = QueuePollingIntervals.DefaultMaximum;

        private readonly IStorageQueue _queue;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly TextWriter _log;
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionExecutor _executor;

        public HostMessageListenerFactory(IStorageQueue queue,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            TextWriter log,
            IFunctionIndexLookup functionLookup,
            IFunctionInstanceLogger functionInstanceLogger,
            IFunctionExecutor executor)
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

            if (log == null)
            {
                throw new ArgumentNullException("log");
            }

            if (functionLookup == null)
            {
                throw new ArgumentNullException("functionLookup");
            }

            if (functionInstanceLogger == null)
            {
                throw new ArgumentNullException("functionInstanceLogger");
            }

            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            _queue = queue;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _log = log;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
            _executor = executor;
        }

        public Task<IListener> CreateAsync(ListenerFactoryContext context)
        {
            ITriggerExecutor<IStorageQueueMessage> triggerExecutor = new HostMessageExecutor(_executor, _functionLookup, _functionInstanceLogger);

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
                log: _log,
                sharedWatcher: null,
                queueConfiguration: _queueConfiguration);

            return Task.FromResult(listener);
        }
    }
}
