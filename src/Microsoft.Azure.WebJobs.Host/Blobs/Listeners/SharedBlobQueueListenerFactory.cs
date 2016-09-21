// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobQueueListenerFactory : IFactory<SharedBlobQueueListener>
    {
        private readonly SharedQueueWatcher _sharedQueueWatcher;
        private readonly IStorageQueue _poisonQueue;
        private readonly IStorageQueue _hostBlobTriggerQueue;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly TraceWriter _trace;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;

        public SharedBlobQueueListenerFactory(
            SharedQueueWatcher sharedQueueWatcher,
            IStorageQueue hostBlobTriggerQueue,
            IStorageQueue poisonQueue,
            IQueueConfiguration queueConfiguration,
            IWebJobsExceptionHandler exceptionHandler,
            TraceWriter trace,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            if (sharedQueueWatcher == null)
            {
                throw new ArgumentNullException("sharedQueueWatcher");
            }

            if (poisonQueue == null)
            {
                throw new ArgumentNullException("poisonQueue");
            }

            if (hostBlobTriggerQueue == null)
            {
                throw new ArgumentNullException("hostBlobTriggerQueue");
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

            if (blobWrittenWatcher == null)
            {
                throw new ArgumentNullException("blobWrittenWatcher");
            }

            _sharedQueueWatcher = sharedQueueWatcher;
            _poisonQueue = poisonQueue;
            _hostBlobTriggerQueue = hostBlobTriggerQueue;
            _queueConfiguration = queueConfiguration;
            _exceptionHandler = exceptionHandler;
            _trace = trace;
            _blobWrittenWatcher = blobWrittenWatcher;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public SharedBlobQueueListener Create()
        {
            BlobQueueTriggerExecutor triggerExecutor =
                new BlobQueueTriggerExecutor(_blobWrittenWatcher);
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(QueuePollingIntervals.Minimum,
                _queueConfiguration.MaxPollingInterval);
            IListener listener = new QueueListener(_hostBlobTriggerQueue, _poisonQueue, triggerExecutor,
                delayStrategy, _exceptionHandler, _trace, _sharedQueueWatcher, _queueConfiguration);
            return new SharedBlobQueueListener(listener, triggerExecutor);
        }
    }
}
