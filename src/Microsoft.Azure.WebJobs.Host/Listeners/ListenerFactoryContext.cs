// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class ListenerFactoryContext
    {
        private readonly HostBindingContext _hostContext;
        private readonly string _hostId;
        private readonly SharedListenerContainer _sharedListeners;
        private readonly CancellationToken _cancellationToken;

        public ListenerFactoryContext(HostBindingContext hostContext, string hostId,
            SharedListenerContainer sharedListeners, CancellationToken cancellationToken)
        {
            _hostContext = hostContext;
            _hostId = hostId;
            _sharedListeners = sharedListeners;
            _cancellationToken = cancellationToken;
        }

        public IQueueConfiguration QueueConfiguration
        {
            get { return _hostContext.QueueConfiguration; }
        }

        public IStorageAccount StorageAccount
        {
            get { return _hostContext.StorageAccount; }
        }

        public IBlobWrittenWatcher BlobWrittenWatcher
        {
            get { return _hostContext.BlobWrittenWatcher; }
            set { _hostContext.BlobWrittenWatcher = value; }
        }

        public IMessageEnqueuedWatcher MessageEnqueuedWatcher
        {
            get { return _hostContext.MessageEnqueuedWatcher; }
            set { _hostContext.MessageEnqueuedWatcher = value; }
        }

        public string HostId
        {
            get { return _hostId; }
        }

        public SharedListenerContainer SharedListeners
        {
            get { return _sharedListeners; }
        }

        public CancellationToken CancellationToken
        {
            get { return _cancellationToken; }
        }
    }
}
