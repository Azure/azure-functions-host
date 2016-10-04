// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class SharedBlobListenerFactory : IFactory<SharedBlobListener>
    {
        private readonly IStorageAccount _account;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly IContextSetter<IBlobWrittenWatcher> _blobWrittenWatcherSetter;
        private readonly string _hostId;

        public SharedBlobListenerFactory(string hostId, IStorageAccount account,
            IWebJobsExceptionHandler exceptionHandler,
            IContextSetter<IBlobWrittenWatcher> blobWrittenWatcherSetter)
        {
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }

            if (exceptionHandler == null)
            {
                throw new ArgumentNullException("exceptionHandler");
            }

            if (blobWrittenWatcherSetter == null)
            {
                throw new ArgumentNullException("blobWrittenWatcherSetter");
            }

            _hostId = hostId;
            _account = account;
            _exceptionHandler = exceptionHandler;
            _blobWrittenWatcherSetter = blobWrittenWatcherSetter;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        public SharedBlobListener Create()
        {
            SharedBlobListener listener = new SharedBlobListener(_hostId, _account, _exceptionHandler);
            _blobWrittenWatcherSetter.SetValue(listener.BlobWritterWatcher);
            return listener;
        }
    }
}
