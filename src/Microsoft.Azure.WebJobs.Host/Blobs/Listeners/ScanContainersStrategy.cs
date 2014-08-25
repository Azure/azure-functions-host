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
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.WebJobs.Host.Blobs.Listeners
{
    internal class ScanContainersStrategy : IBlobNotificationStrategy
    {
        private static readonly TimeSpan _twoSeconds = TimeSpan.FromSeconds(2);

        private readonly IDictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>> _registrations;
        private readonly IDictionary<CloudBlobContainer, DateTime> _lastModifiedTimestamps;
        private readonly ConcurrentQueue<ICloudBlob> _blobWrittenNotifications;

        public ScanContainersStrategy()
        {
            _registrations = new Dictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>>(
                new CloudContainerComparer());
            _lastModifiedTimestamps = new Dictionary<CloudBlobContainer, DateTime>(new CloudContainerComparer());
            _blobWrittenNotifications = new ConcurrentQueue<ICloudBlob>();
        }

        public void Notify(ICloudBlob blobWritten)
        {
            _blobWrittenNotifications.Enqueue(blobWritten);
        }

        public void Register(CloudBlobContainer container, ITriggerExecutor<ICloudBlob> triggerExecutor)
        {
            // Register and Execute are not concurrency-safe.
            // Avoiding calling Register while Execute is running is the caller's responsibility.
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

            if (!_lastModifiedTimestamps.ContainsKey(container))
            {
                _lastModifiedTimestamps.Add(container, DateTime.MinValue);
            }
        }

        public async Task<TaskSeriesCommandResult> ExecuteAsync(CancellationToken cancellationToken)
        {
            List<ICloudBlob> failedNotifications = new List<ICloudBlob>();

            // Drain the background queue of blob written notifications.
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ICloudBlob blob;

                if (!_blobWrittenNotifications.TryDequeue(out blob))
                {
                    break;
                }

                await NotifyRegistrationsAsync(blob, failedNotifications, cancellationToken);
            }

            foreach (CloudBlobContainer container in _registrations.Keys)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DateTime lastScanTimestamp = _lastModifiedTimestamps[container];
                Tuple<IEnumerable<ICloudBlob>, DateTime> newBlobsResult = await PollNewBlobsAsync(container,
                    lastScanTimestamp, cancellationToken);
                IEnumerable<ICloudBlob> newBlobs = newBlobsResult.Item1;
                _lastModifiedTimestamps[container] = newBlobsResult.Item2;

                foreach (ICloudBlob newBlob in newBlobs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await NotifyRegistrationsAsync(newBlob, failedNotifications, cancellationToken);
                }
            }

            // Re-add any failed notifications for the next iteration.
            foreach (ICloudBlob failedNotification in failedNotifications)
            {
                _blobWrittenNotifications.Enqueue(failedNotification);
            }

            // Run subsequent iterations at 2 second intervals.
            return new TaskSeriesCommandResult(wait: Task.Delay(_twoSeconds));
        }

        private async Task NotifyRegistrationsAsync(ICloudBlob blob, ICollection<ICloudBlob> failedNotifications,
            CancellationToken cancellationToken)
        {
            CloudBlobContainer container = blob.Container;

            // Blob written notifications are host-wide, so filter out things that aren't in the container list.
            if (!_registrations.ContainsKey(container))
            {
                return;
            }

            foreach (ITriggerExecutor<ICloudBlob> registration in _registrations[container])
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!await registration.ExecuteAsync(blob, cancellationToken))
                {
                    // If notification failed, try again on the next iteration.
                    failedNotifications.Add(blob);
                }
            }
        }

        public static async Task<Tuple<IEnumerable<ICloudBlob>, DateTime>> PollNewBlobsAsync(
            CloudBlobContainer container, DateTime previousTimestamp, CancellationToken cancellationToken)
        {
            DateTime updatedTimestamp = previousTimestamp;

            IList<IListBlobItem> currentBlobs;

            try
            {
                // async TODO: Use ListBlobsSegmentedAsync in a loop.
                currentBlobs = container.ListBlobs(useFlatBlobListing: true).ToList();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return new Tuple<IEnumerable<ICloudBlob>,DateTime>(
                        Enumerable.Empty<ICloudBlob>(), updatedTimestamp);
                }
                else
                {
                    throw;
                }
            }

            List<ICloudBlob> newBlobs = new List<ICloudBlob>();

            // Type cast to ICloudBlob is safe due to useFlatBlobListing: true above.
            foreach (ICloudBlob currentBlob in currentBlobs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    await currentBlob.FetchAttributesAsync(cancellationToken);
                }
                catch (StorageException exception)
                {
                    if (exception.IsNotFound())
                    {
                        continue;
                    }
                    else
                    {
                        throw;
                    }
                }

                BlobProperties properties = currentBlob.Properties;
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

            return new Tuple<IEnumerable<ICloudBlob>,DateTime>(newBlobs, updatedTimestamp);
        }
    }
}
