// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs.Host.Listeners
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

        public CloudStorageAccount StorageAccount
        {
            get { return _hostContext.StorageAccount; }
        }

        public IBlobWrittenWatcher BlobWrittenWatcher
        {
            get { return _hostContext.BlobWrittenWatcher; }
            set { _hostContext.BlobWrittenWatcher = value; }
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
