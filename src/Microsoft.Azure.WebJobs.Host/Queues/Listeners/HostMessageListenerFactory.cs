// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly TraceWriter _trace;
        private readonly IFunctionIndexLookup _functionLookup;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionExecutor _executor;

        public HostMessageListenerFactory(IStorageQueue queue,
            IQueueConfiguration queueConfiguration,
            IWebJobsExceptionHandler exceptionHandler,
            TraceWriter trace,
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

            if (exceptionHandler == null)
            {
                throw new ArgumentNullException("exceptionHandler");
            }

            if (trace == null)
            {
                throw new ArgumentNullException("trace");
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
            _exceptionHandler = exceptionHandler;
            _trace = trace;
            _functionLookup = functionLookup;
            _functionInstanceLogger = functionInstanceLogger;
            _executor = executor;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public Task<IListener> CreateAsync(CancellationToken cancellationToken)
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
                exceptionHandler: _exceptionHandler,
                trace: _trace,
                sharedWatcher: null,
                queueConfiguration: _queueConfiguration);

            return Task.FromResult(listener);
        }
    }
}
