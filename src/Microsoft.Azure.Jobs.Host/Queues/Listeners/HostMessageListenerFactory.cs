// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class HostMessageListenerFactory : IListenerFactory
    {
        private static readonly TimeSpan Minimum = QueuePollingIntervals.Minimum;
        private static readonly TimeSpan DefaultMaximum = TimeSpan.FromMinutes(1);

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
            IAlertingRecurrentCommand command = new PollQueueCommand(_queue,
                poisonQueue: null,
                triggerExecutor: triggerExecutor,
                sharedWatcher: null,
                maxDequeueCount: queueConfiguration.MaxDequeueCount);
            TimeSpan configuredMaximum = queueConfiguration.MaxPollingInterval;
            // Use a shorter maximum polling interval for run/abort from dashboard.
            // Use the default maximum for host polling (1 minute) unless the configured overall maximum is even faster.
            TimeSpan maximum = configuredMaximum < DefaultMaximum ? configuredMaximum : DefaultMaximum;
            ITaskSeriesTimer timer = RandomizedExponentialBackoffStrategy.CreateTimer(command, Minimum, maximum);
            IListener listener = new TimerListener(timer);
            return Task.FromResult(listener);
        }
    }
}
