// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Listeners;
using Microsoft.Azure.Jobs.Host.Storage;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Blobs.Listeners
{
    // A hybrid strategy that begins with a full container scan and then does incremental updates via log polling.
    internal class PollLogsStrategy : IBlobNotificationStrategy
    {
        private static readonly TimeSpan _twoSeconds = TimeSpan.FromSeconds(2);

        private readonly CancellationToken _cancellationToken;
        private readonly IDictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>> _registrations;
        private readonly IDictionary<CloudBlobClient, BlobLogListener> _logListeners;
        private readonly Thread _initialScanThread;
        private readonly ConcurrentQueue<ICloudBlob> _blobsFoundFromScanOrNotification;

        // Start the first iteration immediately.
        private TimeSpan _separationInterval = TimeSpan.Zero;

        public PollLogsStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registrations = new Dictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>>(
                new CloudContainerComparer());
            _logListeners = new Dictionary<CloudBlobClient, BlobLogListener>(new CloudBlobClientComparer());
            _initialScanThread = new Thread(ScanContainers);
            _blobsFoundFromScanOrNotification =  new ConcurrentQueue<ICloudBlob>();
        }

        public TimeSpan SeparationInterval
        {
            get { return _separationInterval; }
        }

        public void Register(CloudBlobContainer container, ITriggerExecutor<ICloudBlob> triggerExecutor)
        {
            // Initial background scans for all containers happen on first Execute call.
            // Prevent accidental late registrations.
            // (Also prevents incorrect concurrent execution of Register with Execute.)
            if (_initialScanThread.ThreadState != ThreadState.Unstarted)
            {
                throw new InvalidOperationException("All registrations must be created before execution begins.");
            }

            ICollection<ITriggerExecutor<ICloudBlob>> containerRegistrations;

            if (_registrations.ContainsKey(container))
            {
                containerRegistrations = _registrations[container];
            }
            else
            {
                containerRegistrations = new List<ITriggerExecutor<ICloudBlob>>();
                _registrations.Add(container, containerRegistrations);
            }

            containerRegistrations.Add(triggerExecutor);

            CloudBlobClient client = container.ServiceClient;

            if (!_logListeners.ContainsKey(client))
            {
                _logListeners.Add(client, new BlobLogListener(client));
            }
        }

        public void Notify(ICloudBlob blobWritten)
        {
            _blobsFoundFromScanOrNotification.Enqueue(blobWritten);
        }

        public void Execute()
        {
            // Run subsequent iterations at 2 second intervals.
            _separationInterval = _twoSeconds;

            // Start a background scan of the container on first execution. Later writes will be found via polling logs.
            if (_initialScanThread.ThreadState == ThreadState.Unstarted)
            {
                // Thread monitors _cancellationToken (that's the only way this thread is controlled).
                _initialScanThread.Start();
            }

            // Drain the background queue (for initial container scans and blob written notifications).
            while (true)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ICloudBlob blob;

                if (!_blobsFoundFromScanOrNotification.TryDequeue(out blob))
                {
                    break;
                }

                NotifyRegistrations(blob);
            }

            // Poll the logs (to detect ongoing writes).
            foreach (BlobLogListener logListener in _logListeners.Values)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                foreach (ICloudBlob blob in logListener.GetRecentBlobWrites())
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    NotifyRegistrations(blob);
                }
            }
        }

        private void NotifyRegistrations(ICloudBlob blob)
        {
            CloudBlobContainer container = blob.Container;

            // Log listening is client-wide and blob written notifications are host-wide, so filter out things that
            // aren't in the container list.
            if (!_registrations.ContainsKey(container))
            {
                return;
            }

            foreach (ITriggerExecutor<ICloudBlob> registration in _registrations[container])
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                registration.Execute(blob);
            }
        }

        private void ScanContainers()
        {
            foreach (CloudBlobContainer container in _registrations.Keys)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                List<IListBlobItem> items;

                try
                {
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
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    _blobsFoundFromScanOrNotification.Enqueue(item);
                }
            }
        }
    }
}
