// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Blobs;
using Microsoft.Azure.WebJobs.Host.Queues;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Listeners
{
    internal class ListenerFactoryContext
    {
        private readonly HostBindingContext _hostContext;
        private readonly SharedListenerContainer _sharedListeners;
        private readonly CancellationToken _cancellationToken;

        public ListenerFactoryContext(HostBindingContext hostContext, SharedListenerContainer sharedListeners,
            CancellationToken cancellationToken)
        {
            _hostContext = hostContext;
            _sharedListeners = sharedListeners;
            _cancellationToken = cancellationToken;
        }

        public IBackgroundExceptionDispatcher BackgroundExceptionDispatcher
        {
            get { return _hostContext.BackgroundExceptionDispatcher; }
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
