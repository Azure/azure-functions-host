// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
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
        private static readonly TimeSpan maxmimum = TimeSpan.FromMinutes(1);

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
            ICanFailCommand command = new PollQueueCommand(_queue, poisonQueue: null, triggerExecutor: triggerExecutor);
            // Use a shorter maximum polling interval for run/abort from dashboard.
            IntervalSeparationTimer timer = ExponentialBackoffTimerCommand.CreateTimer(command,
                QueuePollingIntervals.Minimum, maxmimum);
            IListener listener = new TimerListener(timer);
            return Task.FromResult(listener);
        }
    }
}
