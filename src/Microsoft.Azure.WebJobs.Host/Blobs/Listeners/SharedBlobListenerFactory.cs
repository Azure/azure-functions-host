// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobListenerFactory : IFactory<SharedBlobListener>
    {
        private readonly IStorageAccount _account;
        private readonly ListenerFactoryContext _context;

        public SharedBlobListenerFactory(IStorageAccount account, ListenerFactoryContext context)
        {
            _account = account;
            _context = context;
        }

        public SharedBlobListener Create()
        {
            SharedBlobListener listener = new SharedBlobListener(_account.SdkObject,
                _context.BackgroundExceptionDispatcher);
            _context.BlobWrittenWatcher = listener.BlobWritterWatcher;
            return listener;
        }
    }
}
