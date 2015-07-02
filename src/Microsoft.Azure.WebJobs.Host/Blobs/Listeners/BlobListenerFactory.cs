// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Queues.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Storage.Queue;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class BlobListenerFactory : IListenerFactory
    {
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IContextSetter<IBlobWrittenWatcher> _blobWrittenWatcherSetter;
        private readonly IContextSetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherSetter;
        private readonly ISharedContextProvider _sharedContextProvider;
        private readonly TextWriter _log;
        private readonly string _functionId;
        private readonly IStorageAccount _account;
        private readonly IStorageBlobContainer _container;
        private readonly IBlobPathSource _input;
        private readonly ITriggeredFunctionExecutor _executor;

        public BlobListenerFactory(IHostIdProvider hostIdProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IContextSetter<IBlobWrittenWatcher> blobWrittenWatcherSetter,
            IContextSetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherSetter,
            ISharedContextProvider sharedContextProvider,
            TextWriter log,
            string functionId,
            IStorageAccount account,
            IStorageBlobContainer container,
            IBlobPathSource input,
            ITriggeredFunctionExecutor executor)
        {
            if (hostIdProvider == null)
            {
                throw new ArgumentNullException("hostIdProvider");
            }

            if (queueConfiguration == null)
            {
                throw new ArgumentNullException("queueConfiguration");
            }

            if (backgroundExceptionDispatcher == null)
            {
                throw new ArgumentNullException("backgroundExceptionDispatcher");
            }

            if (blobWrittenWatcherSetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherSetter");
            }

            if (messageEnqueuedWatcherSetter == null)
            {
                throw new ArgumentNullException("messageEnqueuedWatcherSetter");
            }

            if (sharedContextProvider == null)
            {
                throw new ArgumentNullException("sharedContextProvider");
            }

            if (log == null)
            {
                throw new ArgumentNullException("log");
            }

            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            if (container == null)
            {
                throw new ArgumentNullException("container");
            }

            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            if (executor == null)
            {
                throw new ArgumentNullException("executor");
            }

            _hostIdProvider = hostIdProvider;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _blobWrittenWatcherSetter = blobWrittenWatcherSetter;
            _messageEnqueuedWatcherSetter = messageEnqueuedWatcherSetter;
            _sharedContextProvider = sharedContextProvider;
            _log = log;
            _functionId = functionId;
            _account = account;
            _container = container;
            _input = input;
            _executor = executor;
        }

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            SharedQueueWatcher sharedQueueWatcher = _sharedContextProvider.GetOrCreate<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(_messageEnqueuedWatcherSetter));
            SharedBlobListener sharedBlobListener = _sharedContextProvider.GetOrCreate<SharedBlobListener>(
                new SharedBlobListenerFactory(_account, _backgroundExceptionDispatcher, _blobWrittenWatcherSetter));

            // Note that these clients are intentionally for the storage account rather than for the dashboard account.
            // We use the storage, not dashboard, account for the blob receipt container and blob trigger queues.
            IStorageQueueClient queueClient = _account.CreateQueueClient();
            IStorageBlobClient blobClient = _account.CreateBlobClient();

            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            string hostBlobTriggerQueueName = HostQueueNames.GetHostBlobTriggerQueueName(hostId);
            IStorageQueue hostBlobTriggerQueue = queueClient.GetQueueReference(hostBlobTriggerQueueName);

            IListener blobDiscoveryToQueueMessageListener = await CreateBlobDiscoveryToQueueMessageListenerAsync(
                hostId, sharedBlobListener, blobClient, hostBlobTriggerQueue, sharedQueueWatcher, cancellationToken);
            IListener queueMessageToTriggerExecutionListener = CreateQueueMessageToTriggerExecutionListener(
                _sharedContextProvider, sharedQueueWatcher, queueClient, hostBlobTriggerQueue, blobClient,
                sharedBlobListener.BlobWritterWatcher);
            IListener compositeListener = new CompositeListener(
                blobDiscoveryToQueueMessageListener,
                queueMessageToTriggerExecutionListener);

            return compositeListener;
        }

        private async Task<IListener> CreateBlobDiscoveryToQueueMessageListenerAsync(string hostId,
            SharedBlobListener sharedBlobListener,
            IStorageBlobClient blobClient,
            IStorageQueue hostBlobTriggerQueue,
            IMessageEnqueuedWatcher messageEnqueuedWatcher,
            CancellationToken cancellationToken)
        {
            BlobTriggerExecutor triggerExecutor = new BlobTriggerExecutor(hostId, _functionId, _input,
                BlobETagReader.Instance, new BlobReceiptManager(blobClient),
                new BlobTriggerQueueWriter(hostBlobTriggerQueue, messageEnqueuedWatcher));
            await sharedBlobListener.RegisterAsync(_container, triggerExecutor, cancellationToken);
            return new BlobListener(sharedBlobListener);
        }

        private IListener CreateQueueMessageToTriggerExecutionListener(
            ISharedContextProvider sharedContextProvider,
            SharedQueueWatcher sharedQueueWatcher,
            IStorageQueueClient queueClient,
            IStorageQueue hostBlobTriggerQueue,
            IStorageBlobClient blobClient,
            IBlobWrittenWatcher blobWrittenWatcher)
        {
            SharedBlobQueueListener sharedListener = sharedContextProvider.GetOrCreate<SharedBlobQueueListener>(
                new SharedBlobQueueListenerFactory(sharedQueueWatcher, queueClient, hostBlobTriggerQueue,
                    blobClient, _queueConfiguration, _backgroundExceptionDispatcher, _log, blobWrittenWatcher));

            sharedListener.Register(_functionId, _executor);

            return new BlobListener(sharedListener);
        }
    }
}
