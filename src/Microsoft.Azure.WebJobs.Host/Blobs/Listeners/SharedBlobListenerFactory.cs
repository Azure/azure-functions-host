// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobListenerFactory : IFactory<SharedBlobListener>
    {
        private readonly IStorageAccount _account;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly IContextSetter<IBlobWrittenWatcher> _blobWrittenWatcherSetter;
        private readonly ListenerFactoryContext _context;

        public SharedBlobListenerFactory(IStorageAccount account,
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher,
            IContextSetter<IBlobWrittenWatcher> blobWrittenWatcherSetter,
            ListenerFactoryContext context)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            if (backgroundExceptionDispatcher == null)
            {
                throw new ArgumentNullException("backgroundExceptionDispatcher");
            }

            if (blobWrittenWatcherSetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherSetter");
            }

            if (context == null)
            {
                throw new ArgumentNullException("context");
            }

            _account = account;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _blobWrittenWatcherSetter = blobWrittenWatcherSetter;
            _context = context;
        }

        public SharedBlobListener Create()
        {
            SharedBlobListener listener = new SharedBlobListener(_account.SdkObject, _backgroundExceptionDispatcher);
            _blobWrittenWatcherSetter.SetValue(listener.BlobWritterWatcher);
            return listener;
        }
    }
}
