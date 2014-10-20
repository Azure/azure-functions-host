// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    // A hybrid strategy that begins with a full container scan and then does incremental updates via log polling.
    internal sealed class PollLogsStrategy : IBlobListenerStrategy
    {
        private static readonly TimeSpan _twoSeconds = TimeSpan.FromSeconds(2);

        private readonly IDictionary<CloudBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>> _registrations;
        private readonly IDictionary<CloudBlobClient, BlobLogListener> _logListeners;
        private readonly Thread _initialScanThread;
        private readonly ConcurrentQueue<ICloudBlob> _blobsFoundFromScanOrNotification;
        private readonly CancellationTokenSource _cancellationTokenSource;

        private bool _disposed;

        public PollLogsStrategy()
        {
            _registrations = new Dictionary<CloudBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>>(
                new CloudBlobContainerComparer());
            _logListeners = new Dictionary<CloudBlobClient, BlobLogListener>(new CloudBlobClientComparer());
            _initialScanThread = new Thread(ScanContainers);
            _blobsFoundFromScanOrNotification = new ConcurrentQueue<ICloudBlob>();
            _cancellationTokenSource = new CancellationTokenSource();
        }

        public async Task RegisterAsync(CloudBlobContainer container, ITriggerExecutor<IStorageBlob> triggerExecutor,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            // Initial background scans for all containers happen on first Execute call.
            // Prevent accidental late registrations.
            // (Also prevents incorrect concurrent execution of Register with Execute.)
            if (_initialScanThread.ThreadState != ThreadState.Unstarted)
            {
                throw new InvalidOperationException("All registrations must be created before execution begins.");
            }

            ICollection<ITriggerExecutor<IStorageBlob>> containerRegistrations;

            if (_registrations.ContainsKey(container))
            {
                containerRegistrations = _registrations[container];
            }
            else
            {
                containerRegistrations = new List<ITriggerExecutor<IStorageBlob>>();
                _registrations.Add(container, containerRegistrations);
            }

            containerRegistrations.Add(triggerExecutor);

            CloudBlobClient client = container.ServiceClient;

            if (!_logListeners.ContainsKey(client))
            {
                BlobLogListener logListener = await BlobLogListener.CreateAsync(client, cancellationToken);
                _logListeners.Add(client, logListener);
            }
        }

        public void Notify(ICloudBlob blobWritten)
        {
            ThrowIfDisposed();
            _blobsFoundFromScanOrNotification.Enqueue(blobWritten);
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            // Drain the background queue (for initial container scans and blob written notifications).
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ICloudBlob blob;

                if (!_blobsFoundFromScanOrNotification.TryDequeue(out blob))
                {
                    break;
                }

                await NotifyRegistrationsAsync(blob, cancellationToken);
            }

            // Poll the logs (to detect ongoing writes).
            foreach (BlobLogListener logListener in _logListeners.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();

                foreach (ICloudBlob blob in await logListener.GetRecentBlobWritesAsync(cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await NotifyRegistrationsAsync(blob, cancellationToken);
                }
            }

            // Run subsequent iterations at 2 second intervals.
            return new TaskSeriesCommandResult(wait: Task.Delay(_twoSeconds));
        }

        public void Start()
        {
            ThrowIfDisposed();

            // Start a background scan of the container on first execution. Later writes will be found via polling logs.
            // Thread monitors _cancellationTokenSource.Token (that's the only way this thread is controlled).
            _initialScanThread.Start(_cancellationTokenSource.Token);
        }

        public void Cancel()
        {
            ThrowIfDisposed();
            _cancellationTokenSource.Cancel();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource.Dispose();
                _disposed = true;
            }
        }

        private async Task NotifyRegistrationsAsync(ICloudBlob blob, CancellationToken cancellationToken)
        {
            CloudBlobContainer container = blob.Container;

            // Log listening is client-wide and blob written notifications are host-wide, so filter out things that
            // aren't in the container list.
            if (!_registrations.ContainsKey(container))
            {
                return;
            }

            foreach (ITriggerExecutor<IStorageBlob> registration in _registrations[container])
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await registration.ExecuteAsync(CreateStorageBlob(blob), cancellationToken))
                {
                    // If notification failed, try again on the next iteration.
                    _blobsFoundFromScanOrNotification.Enqueue(blob);
                }
            }
        }

        internal static IStorageBlob CreateStorageBlob(ICloudBlob blob)
        {
            if (blob == null)
            {
                return null;
            }

            IStorageBlobContainer container = new StorageBlobContainer(blob.Container);

            CloudBlockBlob blockBlob = blob as CloudBlockBlob;

            if (blockBlob != null)
            {
                return new StorageBlockBlob(container, blockBlob);
            }
            else
            {
                return new StoragePageBlob(container, (CloudPageBlob)blob);
            }
        }

        private void ScanContainers(object state)
        {
            CancellationToken cancellationToken = (CancellationToken)state;

            foreach (CloudBlobContainer container in _registrations.Keys)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                List<IListBlobItem> items;

                try
                {
                    // Non-async is correct here. ScanContainers occurs on a background thread. Unless it blocks, no one
                    // else is around to observe the results.
                    items = container.ListBlobs(useFlatBlobListing: true).ToList();
                }
                catch (StorageException exception)
                {
                    if (exception.IsNotFound())
                    {
                        return;
                    }
                    else
                    {
                        throw;
                    }
                }

                // Type cast to ICloudBlob is safe due to useFlatBlobListing: true above.
                foreach (ICloudBlob item in items)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _blobsFoundFromScanOrNotification.Enqueue(item);
                }
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }
    }
}
