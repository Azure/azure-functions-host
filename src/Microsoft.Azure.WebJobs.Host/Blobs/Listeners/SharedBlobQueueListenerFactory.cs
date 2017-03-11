// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage.Queue;
using Newtonsoft.Json;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobQueueListenerFactory : IFactory<SharedBlobQueueListener>
    {
        private readonly SharedQueueWatcher _sharedQueueWatcher;
        private readonly IStorageQueue _hostBlobTriggerQueue;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly TraceWriter _trace;
        private readonly IBlobWrittenWatcher _blobWrittenWatcher;
        private readonly IStorageAccount _hostAccount;
        private readonly ILoggerFactory _loggerFactory;

        public SharedBlobQueueListenerFactory(
            IStorageAccount hostAccount,
            SharedQueueWatcher sharedQueueWatcher,
            IStorageQueue hostBlobTriggerQueue,
            IQueueConfiguration queueConfiguration,
            IWebJobsExceptionHandler exceptionHandler,
            TraceWriter trace,
            ILoggerFactory loggerFactory,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            if (hostAccount == null)
            {
                throw new ArgumentNullException("hostAccount");
            }

            if (sharedQueueWatcher == null)
            {
                throw new ArgumentNullException("sharedQueueWatcher");
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

            _hostAccount = hostAccount;
            _sharedQueueWatcher = sharedQueueWatcher;
            _hostBlobTriggerQueue = hostBlobTriggerQueue;
            _queueConfiguration = queueConfiguration;
            _exceptionHandler = exceptionHandler;
            _trace = trace;
            _loggerFactory = loggerFactory;
            _blobWrittenWatcher = blobWrittenWatcher;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public SharedBlobQueueListener Create()
        {
            BlobQueueTriggerExecutor triggerExecutor = new BlobQueueTriggerExecutor(_blobWrittenWatcher);

            // The poison queue to use for a given poison blob lives in the same
            // storage account as the triggering blob by default. In multi-storage account scenarios
            // that means that we'll be writing to different poison queues, determined by
            // the triggering blob.
            // However we use a poison queue in the host storage account as a fallback default
            // in case a particular blob lives in a restricted "blob only" storage account (i.e. no queues).
            IStorageQueue defaultPoisonQueue = _hostAccount.CreateQueueClient().GetQueueReference(HostQueueNames.BlobTriggerPoisonQueue);

            // this special queue bypasses the QueueProcessorFactory - we don't want people to
            // override this
            QueueProcessorFactoryContext context = new QueueProcessorFactoryContext(_hostBlobTriggerQueue.SdkObject, _trace, _loggerFactory,
                _queueConfiguration, defaultPoisonQueue.SdkObject);
            SharedBlobQueueProcessor queueProcessor = new SharedBlobQueueProcessor(context, triggerExecutor);

            IListener listener = new QueueListener(_hostBlobTriggerQueue, defaultPoisonQueue, triggerExecutor,
                _exceptionHandler, _trace, _loggerFactory, _sharedQueueWatcher, _queueConfiguration, queueProcessor);

            return new SharedBlobQueueListener(listener, triggerExecutor);
        }

        /// <summary>
        /// Custom queue processor for the shared blob queue.
        /// </summary>
        private class SharedBlobQueueProcessor : QueueProcessor
        {
            private BlobQueueTriggerExecutor _executor;

            public SharedBlobQueueProcessor(QueueProcessorFactoryContext context, BlobQueueTriggerExecutor executor) : base(context)
            {
                _executor = executor;
            }

            protected override Task CopyMessageToPoisonQueueAsync(CloudQueueMessage message, CloudQueue poisonQueue, CancellationToken cancellationToken)
            {
                // determine the poison queue based on the storage account
                // of the triggering blob, or default
                poisonQueue = GetPoisonQueue(message) ?? poisonQueue;

                return base.CopyMessageToPoisonQueueAsync(message, poisonQueue, cancellationToken);
            }

            private CloudQueue GetPoisonQueue(CloudQueueMessage message)
            {
                if (message == null)
                {
                    throw new ArgumentNullException("message");
                }

                var blobTriggerMessage = JsonConvert.DeserializeObject<BlobTriggerMessage>(message.AsString);

                BlobQueueRegistration registration = null;
                if (_executor.TryGetRegistration(blobTriggerMessage.FunctionId, out registration))
                {
                    IStorageQueue poisonQueue = registration.QueueClient.GetQueueReference(HostQueueNames.BlobTriggerPoisonQueue);
                    return poisonQueue.SdkObject;
                }

                return null;
            }
        }
    }
}
