// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobListenerFactory : IFactory<SharedBlobListener>
    {
        private readonly ListenerFactoryContext _context;

        public SharedBlobListenerFactory(ListenerFactoryContext context)
        {
            _context = context;
        }

        public SharedBlobListener Create()
        {
            SharedBlobListener listener = new SharedBlobListener(_context.StorageAccount.SdkObject,
                _context.BackgroundExceptionDispatcher);
            _context.BlobWrittenWatcher = listener.BlobWritterWatcher;
            return listener;
        }
    }
}
