// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    internal sealed class SharedBlobListener : ISharedListener<CloudBlobContainer, ICloudBlob>
    {
        private readonly IBlobNotificationStrategy _strategy;
        private readonly IntervalSeparationTimer _timer;

        private bool _started;
        private bool _disposed;

        public SharedBlobListener(CloudStorageAccount storageAccount, CancellationToken cancellationToken)
        {
            _strategy = CreateStrategy(storageAccount, cancellationToken);
            _timer = new IntervalSeparationTimer(_strategy);
        }

        public IBlobWrittenWatcher BlobWritterWatcher
        {
            get { return _strategy; }
        }

        public void Register(CloudBlobContainer listenData, ITriggerExecutor<ICloudBlob> triggerExecutor)
        {
            if (_started)
            {
                throw new InvalidOperationException("Registrations may not be added while the shared listener is running.");
            }

            _strategy.Register(listenData, triggerExecutor);
        }

        public void EnsureAllStarted()
        {
            if (!_started)
            {
                _timer.Start(executeFirst: false);
                _started = true;
            }
        }

        public void EnsureAllStopped()
        {
            if (_started)
            {
                _timer.Stop();
                _started = false;
            }
        }

        public void EnsureAllDisposed()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _timer.Dispose();
                _disposed = true;
            }
        }

        private static IBlobNotificationStrategy CreateStrategy(CloudStorageAccount account,
            CancellationToken cancellationToken)
        {
            if (!StorageClient.IsDevelopmentStorageAccount(account))
            {
                return new PollLogsStrategy(cancellationToken);
            }
            else
            {
                return new ScanContainersStrategy(cancellationToken);
            }
        }
    }
}
