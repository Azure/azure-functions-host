// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Listeners;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Host.Storage.Blob;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal sealed class ScanBlobScanLogHybridPollingStrategy : IBlobListenerStrategy
    {
        private static readonly TimeSpan PollingtInterval = TimeSpan.FromSeconds(10);
        private readonly IDictionary<IStorageBlobContainer, ContainerScanInfo> _scanInfo;
        private readonly ConcurrentQueue<IStorageBlob> _blobsFoundFromScanOrNotification;
        private readonly CancellationTokenSource _cancellationTokenSource;
        // A budget is allocated representing the number of blobs to be listed in a polling 
        // interval, each container will get its share of _scanBlobLimitPerPoll/number of containers.
        // this share will be listed for each container each polling interval
        private int _scanBlobLimitPerPoll = 10000;
        private PollLogsStrategy _pollLogStrategy;
        private bool _disposed;

        public ScanBlobScanLogHybridPollingStrategy() : base()
        {
            _scanInfo = new Dictionary<IStorageBlobContainer, ContainerScanInfo>(new StorageBlobContainerComparer());
            _pollLogStrategy = new PollLogsStrategy(performInitialScan: false);
            _cancellationTokenSource = new CancellationTokenSource();
            _blobsFoundFromScanOrNotification = new ConcurrentQueue<IStorageBlob>();
        }

        public void Start()
        {
            ThrowIfDisposed();
            _pollLogStrategy.Start();
        }

        public void Cancel()
        {
            ThrowIfDisposed();
            _pollLogStrategy.Cancel();
            _cancellationTokenSource.Cancel();
        }

        public async Task RegisterAsync(IStorageBlobContainer container, ITriggerExecutor<IStorageBlob> triggerExecutor, CancellationToken cancellationToken)
        {
            // Register and Execute are not concurrency-safe.
            // Avoiding calling Register while Execute is running is the caller's responsibility.
            ThrowIfDisposed();

            // Register all in logPolling, there is no problem if we get 2 notifications of the new blob
            await _pollLogStrategy.RegisterAsync(container, triggerExecutor, cancellationToken);

            ContainerScanInfo containerScanInfo;
            if (!_scanInfo.TryGetValue(container, out containerScanInfo))
            {
                containerScanInfo = new ContainerScanInfo()
                {
                    Registrations = new List<ITriggerExecutor<IStorageBlob>>(),
                    LastSweepCycleLatestModified = DateTime.MinValue,
                    CurrentSweepCycleLatestModified = DateTime.MinValue,
                    ContinuationToken = null
                };
                _scanInfo.Add(container, containerScanInfo);
            }

            containerScanInfo.Registrations.Add(triggerExecutor);
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();

            Task logPollingTask = _pollLogStrategy.ExecuteAsync(cancellationToken);
            List<IStorageBlob> failedNotifications = new List<IStorageBlob>();
            List<Task> notifications = new List<Task>();

            // Drain the background queue of blob written notifications.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IStorageBlob blob;

                if (!_blobsFoundFromScanOrNotification.TryDequeue(out blob))
                {
                    break;
                }

                notifications.Add(NotifyRegistrationsAsync(blob, failedNotifications, cancellationToken));
            }
            await Task.WhenAll(notifications);

            List<Task> pollingTasks = new List<Task>();
            pollingTasks.Add(logPollingTask);

            foreach (KeyValuePair<IStorageBlobContainer, ContainerScanInfo> containerScanInfoPair in _scanInfo)
            {
                pollingTasks.Add(PollAndNotify(containerScanInfoPair.Key, containerScanInfoPair.Value, cancellationToken, failedNotifications));
            }

            // Re-add any failed notifications for the next iteration.
            foreach (IStorageBlob failedNotification in failedNotifications)
            {
                _blobsFoundFromScanOrNotification.Enqueue(failedNotification);
            }

            await Task.WhenAll(pollingTasks);

            // Run subsequent iterations at "_pollingInterval" second intervals.
            return new TaskSeriesCommandResult(wait: Task.Delay(PollingtInterval));
        }

        private async Task PollAndNotify(IStorageBlobContainer container, ContainerScanInfo containerInfo, CancellationToken cancellationToken, List<IStorageBlob> failedNotifications)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IEnumerable<IStorageBlob> newBlobs = await PollNewBlobsAsync(container, containerInfo, cancellationToken);

            foreach (IStorageBlob newBlob in newBlobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await NotifyRegistrationsAsync(newBlob, failedNotifications, cancellationToken);
            }
        }

        public void Notify(IStorageBlob blobWritten)
        {
            ThrowIfDisposed();
            _blobsFoundFromScanOrNotification.Enqueue(blobWritten);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _pollLogStrategy.Dispose();
                _cancellationTokenSource.Dispose();
                _disposed = true;
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(null);
            }
        }

        /// <summary>
        /// This method is called each polling interval for all containers. The method divides the 
        /// budget of allocated number of blobs to query, for each container we query a page of 
        /// that size and we keep the continuation token for the next time. AS a curser, we use
        /// the time stamp when the current cycle on the container started. blobs newer than that
        /// time will be considered new and registrations will be notified
        /// </summary>
        /// <param name="container"></param>
        /// <param name="containerScanInfo"> Information that includes the last cycle start
        /// the continuation token and the current cycle start for a container</param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<IEnumerable<IStorageBlob>> PollNewBlobsAsync(
            IStorageBlobContainer container, ContainerScanInfo containerScanInfo, CancellationToken cancellationToken)
        {
            IEnumerable<IStorageListBlobItem> currentBlobs;
            IStorageBlobResultSegment blobSegment;
            int blobPollLimitPerContainer = _scanBlobLimitPerPoll / _scanInfo.Count;
            BlobContinuationToken continuationToken = containerScanInfo.ContinuationToken;

            // if starting the cycle, reset the sweep time
            if (continuationToken == null)
            {
                containerScanInfo.CurrentSweepCycleLatestModified = DateTime.MinValue;
            }
            try
            {
                blobSegment = await container.ListBlobsSegmentedAsync(prefix: null, useFlatBlobListing: true,
                    blobListingDetails: BlobListingDetails.None, maxResults: blobPollLimitPerContainer, currentToken: continuationToken,
                    options: null, operationContext: null, cancellationToken: cancellationToken);
                currentBlobs = blobSegment.Results;
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return Enumerable.Empty<IStorageBlob>();
                }
                else
                {
                    throw;
                }
            }

            List<IStorageBlob> newBlobs = new List<IStorageBlob>();

            // Type cast to IStorageBlob is safe due to useFlatBlobListing: true above.
            foreach (IStorageBlob currentBlob in currentBlobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                IStorageBlobProperties properties = currentBlob.Properties;
                DateTime lastModifiedTimestamp = properties.LastModified.Value.UtcDateTime;

                if (lastModifiedTimestamp > containerScanInfo.CurrentSweepCycleLatestModified)
                {
                    containerScanInfo.CurrentSweepCycleLatestModified = lastModifiedTimestamp;
                }

                if (lastModifiedTimestamp > containerScanInfo.LastSweepCycleLatestModified)
                {
                    newBlobs.Add(currentBlob);
                }
            }

            // record continuation token for next chunk retrieval
            containerScanInfo.ContinuationToken = blobSegment.ContinuationToken;

            // if ending a cycle then copy currentSweepCycleStartTime to lastSweepCycleStartTime
            if (blobSegment.ContinuationToken == null)
            {
                containerScanInfo.LastSweepCycleLatestModified = containerScanInfo.CurrentSweepCycleLatestModified;
            }

            return newBlobs;
        }

        private async Task NotifyRegistrationsAsync(IStorageBlob blob, ICollection<IStorageBlob> failedNotifications, CancellationToken cancellationToken)
        {
            IStorageBlobContainer container = blob.Container;
            ContainerScanInfo containerScanInfo;

            // Blob written notifications are host-wide, so filter out things that aren't in the container list.
            if (!_scanInfo.TryGetValue(container, out containerScanInfo))
            {
                return;
            }

            foreach (ITriggerExecutor<IStorageBlob> registration in containerScanInfo.Registrations)
            {
                cancellationToken.ThrowIfCancellationRequested();

                FunctionResult result = await registration.ExecuteAsync(blob, cancellationToken);
                if (!result.Succeeded)
                {
                    // If notification failed, try again on the next iteration.
                    failedNotifications.Add(blob);
                }
            }
        }
    }
}
