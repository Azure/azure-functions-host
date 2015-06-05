// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobQueueListenerFactory : IFactory<SharedBlobQueueListener>
    {
        private readonly SharedQueueWatcher _sharedQueueWatcher;
        private readonly IStorageQueueClient _queueClient;
        private readonly IStorageQueue _hostBlobTriggerQueue;
        private readonly IStorageBlobClient _blobClient;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly TextWriter _log;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;

        public SharedBlobQueueListenerFactory(
            SharedQueueWatcher sharedQueueWatcher,
            IStorageQueueClient queueClient,
            IStorageQueue hostBlobTriggerQueue,
            IStorageBlobClient blobClient,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            TextWriter log,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            if (sharedQueueWatcher == null)
            {
                throw new ArgumentNullException("sharedQueueWatcher");
            }

            if (queueClient == null)
            {
                throw new ArgumentNullException("queueClient");
            }

            if (hostBlobTriggerQueue == null)
            {
                throw new ArgumentNullException("hostBlobTriggerQueue");
            }

            if (blobClient == null)
            {
                throw new ArgumentNullException("blobClient");
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

            if (blobWrittenWatcher == null)
            {
                throw new ArgumentNullException("blobWrittenWatcher");
            }

            _sharedQueueWatcher = sharedQueueWatcher;
            _queueClient = queueClient;
            _hostBlobTriggerQueue = hostBlobTriggerQueue;
            _blobClient = blobClient;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _log = log;
            _blobWrittenWatcher = blobWrittenWatcher;
        }

        public SharedBlobQueueListener Create()
        {
            IStorageQueue blobTriggerPoisonQueue =
                _queueClient.GetQueueReference(HostQueueNames.BlobTriggerPoisonQueue);
            BlobQueueTriggerExecutor triggerExecutor =
                new BlobQueueTriggerExecutor(_blobClient, _blobWrittenWatcher);
            IDelayStrategy delayStrategy = new RandomizedExponentialBackoffStrategy(QueuePollingIntervals.Minimum,
                _queueConfiguration.MaxPollingInterval);
            IListener listener = new QueueListener(_hostBlobTriggerQueue, blobTriggerPoisonQueue, triggerExecutor,
                delayStrategy, _backgroundExceptionDispatcher, _log, _sharedQueueWatcher, _queueConfiguration);
            return new SharedBlobQueueListener(listener, triggerExecutor);
        }
    }
}
