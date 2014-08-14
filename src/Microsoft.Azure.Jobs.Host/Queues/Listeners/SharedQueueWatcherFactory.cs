// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.Azure.Jobs.Host.Listeners;

namespace Microsoft.Azure.Jobs.Host.Queues.Listeners
{
    internal class SharedQueueWatcherFactory : IFactory<SharedQueueWatcher>
    {
        private readonly ListenerFactoryContext _context;

        public SharedQueueWatcherFactory(ListenerFactoryContext context)
        {
            _context = context;
        }

        public SharedQueueWatcher Create()
        {
            SharedQueueWatcher watcher = new SharedQueueWatcher();
            _context.MessageEnqueuedWatcher = watcher;
            return watcher;
        }
    }
}
