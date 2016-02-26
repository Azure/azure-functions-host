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
    internal sealed class ScanContainersStrategy : IBlobListenerStrategy
    {
        private static readonly TimeSpan TwoSeconds = TimeSpan.FromSeconds(2);

        private readonly IDictionary<IStorageBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>> _registrations;
        private readonly IDictionary<IStorageBlobContainer, DateTime> _lastModifiedTimestamps;
        private readonly ConcurrentQueue<IStorageBlob> _blobWrittenNotifications;

        public ScanContainersStrategy()
        {
            _registrations = new Dictionary<IStorageBlobContainer, ICollection<ITriggerExecutor<IStorageBlob>>>(
                new StorageBlobContainerComparer());
            _lastModifiedTimestamps = new Dictionary<IStorageBlobContainer, DateTime>(
                new StorageBlobContainerComparer());
            _blobWrittenNotifications = new ConcurrentQueue<IStorageBlob>();
        }

        public void Notify(IStorageBlob blobWritten)
        {
            _blobWrittenNotifications.Enqueue(blobWritten);
        }

        public Task RegisterAsync(IStorageBlobContainer container, ITriggerExecutor<IStorageBlob> triggerExecutor,
            CancellationToken cancellationToken)
        {
            // Register and Execute are not concurrency-safe.
            // Avoiding calling Register while Execute is running is the caller's responsibility.
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

            if (!_lastModifiedTimestamps.ContainsKey(container))
            {
                _lastModifiedTimestamps.Add(container, DateTime.MinValue);
            }

            return Task.FromResult(0);
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            List<IStorageBlob> failedNotifications = new List<IStorageBlob>();

            // Drain the background queue of blob written notifications.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                IStorageBlob blob;

                if (!_blobWrittenNotifications.TryDequeue(out blob))
                {
                    break;
                }

                await NotifyRegistrationsAsync(blob, failedNotifications, cancellationToken);
            }

            foreach (IStorageBlobContainer container in _registrations.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTime lastScanTimestamp = _lastModifiedTimestamps[container];
                Tuple<IEnumerable<IStorageBlob>, DateTime> newBlobsResult = await PollNewBlobsAsync(container,
                    lastScanTimestamp, cancellationToken);
                IEnumerable<IStorageBlob> newBlobs = newBlobsResult.Item1;
                _lastModifiedTimestamps[container] = newBlobsResult.Item2;

                foreach (IStorageBlob newBlob in newBlobs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await NotifyRegistrationsAsync(newBlob, failedNotifications, cancellationToken);
                }
            }

            // Re-add any failed notifications for the next iteration.
            foreach (IStorageBlob failedNotification in failedNotifications)
            {
                _blobWrittenNotifications.Enqueue(failedNotification);
            }

            // Run subsequent iterations at 2 second intervals.
            return new TaskSeriesCommandResult(wait: Task.Delay(TwoSeconds));
        }

        public void Cancel()
        {
        }

        public void Start()
        {
        }

        public void Dispose()
        {
        }

        private async Task NotifyRegistrationsAsync(IStorageBlob blob, ICollection<IStorageBlob> failedNotifications,
            CancellationToken cancellationToken)
        {
            IStorageBlobContainer container = blob.Container;

            // Blob written notifications are host-wide, so filter out things that aren't in the container list.
            if (!_registrations.ContainsKey(container))
            {
                return;
            }

            foreach (ITriggerExecutor<IStorageBlob> registration in _registrations[container])
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

        public static async Task<Tuple<IEnumerable<IStorageBlob>, DateTime>> PollNewBlobsAsync(
            IStorageBlobContainer container, DateTime previousTimestamp, CancellationToken cancellationToken)
        {
            DateTime updatedTimestamp = previousTimestamp;

            IList<IStorageListBlobItem> currentBlobs;

            try
            {
                currentBlobs = (await container.ListBlobsAsync(prefix: null, useFlatBlobListing: true,
                    cancellationToken: cancellationToken)).ToList();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return new Tuple<IEnumerable<IStorageBlob>, DateTime>(
                        Enumerable.Empty<IStorageBlob>(), updatedTimestamp);
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

                if (lastModifiedTimestamp > updatedTimestamp)
                {
                    updatedTimestamp = lastModifiedTimestamp;
                }

                if (lastModifiedTimestamp > previousTimestamp)
                {
                    newBlobs.Add(currentBlob);
                }
            }

            return new Tuple<IEnumerable<IStorageBlob>, DateTime>(newBlobs, updatedTimestamp);
        }
    }
}
