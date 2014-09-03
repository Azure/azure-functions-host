// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobQueueListenerFactory : IFactory<SharedBlobQueueListener>
    {
        private readonly IFunctionExecutor _executor;
        private readonly ListenerFactoryContext _context;
        private readonly SharedQueueWatcher _sharedQueueWatcher;
        private readonly CloudQueueClient _queueClient;
        private readonly CloudQueue _hostBlobTriggerQueue;
        private readonly CloudBlobClient _blobClient;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;

        public SharedBlobQueueListenerFactory(IFunctionExecutor executor,
            ListenerFactoryContext context,
            SharedQueueWatcher sharedQueueWatcher,
            CloudQueueClient queueClient,
            CloudQueue hostBlobTriggerQueue,
            CloudBlobClient blobClient,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            _executor = executor;
            _context = context;
            _sharedQueueWatcher = sharedQueueWatcher;
            _queueClient = queueClient;
            _hostBlobTriggerQueue = hostBlobTriggerQueue;
            _blobClient = blobClient;
            _blobWrittenWatcher = blobWrittenWatcher;
        }

        public SharedBlobQueueListener Create()
        {
            CloudQueue blobTriggerPoisonQueue = _queueClient.GetQueueReference(HostQueueNames.BlobTriggerPoisonQueue);
            BlobQueueTriggerExecutor triggerExecutor =
                new BlobQueueTriggerExecutor(_blobClient, _executor, _blobWrittenWatcher);
            IQueueConfiguration queueConfiguration = _context.QueueConfiguration;
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(QueuePollingIntervals.Minimum,
                queueConfiguration.MaxPollingInterval);
            IListener listener = new QueueListener(_hostBlobTriggerQueue, blobTriggerPoisonQueue, triggerExecutor,
                delayStrategy, _sharedQueueWatcher, queueConfiguration.BatchSize, queueConfiguration.MaxDequeueCount);
            return new SharedBlobQueueListener(listener, triggerExecutor);
        }
    }
}
