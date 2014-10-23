// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Listeners;

namespace Microsoft.Azure.WebJobs.Host.Queues.Listeners
{
    internal class SharedQueueWatcherFactory : IFactory<SharedQueueWatcher>
    {
        private readonly IContextSetter<IMessageEnqueuedWatcher> _messageEnqueuedWatcherSetter;

        public SharedQueueWatcherFactory(IContextSetter<IMessageEnqueuedWatcher> messageEnqueuedWatcherSetter)
        {
            if (messageEnqueuedWatcherSetter == null)
            {
                throw new ArgumentNullException("messageEnqueuedWatcherSetter");
            }

            _messageEnqueuedWatcherSetter = messageEnqueuedWatcherSetter;
        }

        public SharedQueueWatcher Create()
        {
            SharedQueueWatcher watcher = new SharedQueueWatcher();
            _messageEnqueuedWatcherSetter.SetValue(watcher);
            return watcher;
        }
    }
}
