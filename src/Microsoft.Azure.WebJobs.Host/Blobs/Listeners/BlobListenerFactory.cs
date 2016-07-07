// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        private const string SingletonBlobListenerScopeId = "WebJobs.Internal.Blobs";
        private readonly IHostIdProvider _hostIdProvider;
        private readonly IQueueConfiguration _queueConfiguration;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IContextSetter<IBlobWrittenWatcher> _blobWrittenWatcherSetter;
        private readonly IContextSetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherSetter;
        private readonly ISharedContextProvider _sharedContextProvider;
        private readonly TraceWriter _trace;
        private readonly string _functionId;
        private readonly IStorageAccount _hostAccount;
        private readonly IStorageAccount _dataAccount;
        private readonly IStorageBlobContainer _container;
        private readonly IBlobPathSource _input;
        private readonly ITriggeredFunctionExecutor _executor;
        private readonly SingletonManager _singletonManager;

        public BlobListenerFactory(IHostIdProvider hostIdProvider,
            IQueueConfiguration queueConfiguration,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IContextSetter<IBlobWrittenWatcher> blobWrittenWatcherSetter,
            IContextSetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherSetter,
            ISharedContextProvider sharedContextProvider,
            TraceWriter trace,
            string functionId,
            IStorageAccount hostAccount,
            IStorageAccount dataAccount,
            IStorageBlobContainer container,
            IBlobPathSource input,
            ITriggeredFunctionExecutor executor,
            SingletonManager singletonManager)
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

            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            if (hostAccount == null)
            {
                throw new ArgumentNullException("hostAccount");
            }

            if (dataAccount == null)
            {
                throw new ArgumentNullException("dataAccount");
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

            if (singletonManager == null)
            {
                throw new ArgumentNullException("singletonManager");
            }

            _hostIdProvider = hostIdProvider;
            _queueConfiguration = queueConfiguration;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _blobWrittenWatcherSetter = blobWrittenWatcherSetter;
            _messageEnqueuedWatcherSetter = messageEnqueuedWatcherSetter;
            _sharedContextProvider = sharedContextProvider;
            _trace = trace;
            _functionId = functionId;
            _hostAccount = hostAccount;
            _dataAccount = dataAccount;
            _container = container;
            _input = input;
            _executor = executor;
            _singletonManager = singletonManager;
        }

        public async Task<IListener> CreateAsync(CancellationToken cancellationToken)
        {
            SharedQueueWatcher sharedQueueWatcher = _sharedContextProvider.GetOrCreateInstance<SharedQueueWatcher>(
                new SharedQueueWatcherFactory(_messageEnqueuedWatcherSetter));

            SharedBlobListener sharedBlobListener = _sharedContextProvider.GetOrCreateInstance<SharedBlobListener>(
                new SharedBlobListenerFactory(_hostAccount, _backgroundExceptionDispatcher, _blobWrittenWatcherSetter));

            // Note that these clients are intentionally for the storage account rather than for the dashboard account.
            // We use the storage, not dashboard, account for the blob receipt container and blob trigger queues.
            IStorageQueueClient queueClient = _hostAccount.CreateQueueClient();
            IStorageBlobClient blobClient = _hostAccount.CreateBlobClient();

            string hostId = await _hostIdProvider.GetHostIdAsync(cancellationToken);
            string hostBlobTriggerQueueName = HostQueueNames.GetHostBlobTriggerQueueName(hostId);
            IStorageQueue hostBlobTriggerQueue = queueClient.GetQueueReference(hostBlobTriggerQueueName);

            // Register the blob container we wish to monitor with the shared blob listener.
            await RegisterWithSharedBlobListenerAsync(hostId, sharedBlobListener, blobClient, 
                hostBlobTriggerQueue, sharedQueueWatcher, cancellationToken);

            // Create a "bridge" listener that will monitor the blob
            // notification queue and dispatch to the target job function.
            SharedBlobQueueListener sharedBlobQueueListener = _sharedContextProvider.GetOrCreateInstance<SharedBlobQueueListener>(
                new SharedBlobQueueListenerFactory(sharedQueueWatcher, queueClient, hostBlobTriggerQueue,
                    _queueConfiguration, _backgroundExceptionDispatcher, _trace, sharedBlobListener.BlobWritterWatcher));
            var queueListener = new BlobListener(sharedBlobQueueListener);

            // Important: We're using the "data account" here, which is the account that the
            // function the listener is for is targeting. This is the account that will be used
            // to read the trigger blob.
            IStorageBlobClient userBlobClient = _dataAccount.CreateBlobClient();

            // Register our function with the shared queue listener
            RegisterWithSharedBlobQueueListenerAsync(sharedBlobQueueListener, userBlobClient);

            // check a flag in the shared context to see if we've created the singleton
            // shared blob listener in this host instance
            object singletonListenerCreated = false;
            if (!_sharedContextProvider.TryGetValue(SingletonBlobListenerScopeId, out singletonListenerCreated))
            {
                // Create a singleton shared blob listener, since we only
                // want a single instance of the blob poll/scan logic to be running
                // across host instances
                var singletonBlobListener = _singletonManager.CreateHostSingletonListener(
                    new BlobListener(sharedBlobListener), SingletonBlobListenerScopeId);
                _sharedContextProvider.SetValue(SingletonBlobListenerScopeId, true);

                return new CompositeListener(singletonBlobListener, queueListener);
            }
            else
            {
                // We've already created the singleton blob listener
                // so just return our queue listener. Note that while we want the
                // blob scan to be singleton, the shared queue listener needs to run
                // on ALL instances so load can be scaled out
                return queueListener;
            }
        }

        private async Task RegisterWithSharedBlobListenerAsync(
            string hostId,
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
        }

        private void RegisterWithSharedBlobQueueListenerAsync(
            SharedBlobQueueListener sharedBlobQueueListener,
            IStorageBlobClient blobClient)
        {
            BlobQueueRegistration registration = new BlobQueueRegistration
            {
                Executor = _executor,
                BlobClient = blobClient
            };

            sharedBlobQueueListener.Register(_functionId, registration);
        }
    }
}
