// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal sealed class SharedBlobListener : ISharedListener<CloudBlobContainer, ICloudBlob>
    {
        private readonly IBlobNotificationStrategy _strategy;
        private readonly ITaskSeriesTimer _timer;

        private bool _started;
        private bool _disposed;

        public SharedBlobListener(CloudStorageAccount storageAccount)
        {
            _strategy = CreateStrategy(storageAccount);
            // Start the first iteration immediately.
            _timer = new TaskSeriesTimer(_strategy, initialWait: Task.Delay(0));
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
                _timer.Start();
                _started = true;
            }
        }

        public async Task EnsureAllStopped(CancellationToken cancellationToken)
        {
            if (_started)
            {
                await _timer.StopAsync(cancellationToken);
                _started = false;
            }
        }

        public void EnsureAllCanceled()
        {
            _timer.Cancel();
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

        private static IBlobNotificationStrategy CreateStrategy(CloudStorageAccount account)
        {
            if (!StorageClient.IsDevelopmentStorageAccount(account))
            {
                return new PollLogsStrategy();
            }
            else
            {
                return new ScanContainersStrategy();
            }
        }
    }
}
