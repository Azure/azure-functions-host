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
    internal class ScanContainersStrategy : IBlobNotificationStrategy
    {
        private static readonly TimeSpan _twoSeconds = TimeSpan.FromSeconds(2);

        private readonly CancellationToken _cancellationToken;
        private readonly IDictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>> _registrations;
        private readonly IDictionary<CloudBlobContainer, DateTime> _lastModifiedTimestamps;
        private readonly ConcurrentQueue<ICloudBlob> _blobWrittenNotifications;

        // Start the first iteration immediately.
        private TimeSpan _separationInterval = TimeSpan.Zero;

        public ScanContainersStrategy(CancellationToken cancellationToken)
        {
            _cancellationToken = cancellationToken;
            _registrations = new Dictionary<CloudBlobContainer, ICollection<ITriggerExecutor<ICloudBlob>>>(
                new CloudContainerComparer());
            _lastModifiedTimestamps = new Dictionary<CloudBlobContainer, DateTime>(new CloudContainerComparer());
            _blobWrittenNotifications = new ConcurrentQueue<ICloudBlob>();
        }

        public TimeSpan SeparationInterval
        {
            get { return _separationInterval; }
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

        public void Execute()
        {
            // Run subsequent iterations at 2 second intervals.
            _separationInterval = _twoSeconds;

            // Drain the background queue of blob written notifications.
            while (true)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                ICloudBlob blob;

                if (!_blobWrittenNotifications.TryDequeue(out blob))
                {
                    break;
                }

                NotifyRegistrations(blob);
            }

            foreach (CloudBlobContainer container in _registrations.Keys)
            {
                if (_cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                DateTime lastScanTimestamp = _lastModifiedTimestamps[container];
                DateTime updatedTimestamp;
                IEnumerable<ICloudBlob> newBlobs = PollNewBlobs(container, lastScanTimestamp, _cancellationToken,
                    out updatedTimestamp);
                _lastModifiedTimestamps[container] = updatedTimestamp;

                foreach (ICloudBlob newBlob in newBlobs)
                {
                    if (_cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    NotifyRegistrations(newBlob);
                }
            }
        }

        private void NotifyRegistrations(ICloudBlob blob)
        {
            CloudBlobContainer container = blob.Container;

            // Blob written notifications are host-wide, so filter out things that aren't in the container list.
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

        public static IEnumerable<ICloudBlob> PollNewBlobs(CloudBlobContainer container, DateTime previousTimestamp,
            CancellationToken cancellationToken, out DateTime updatedTimestamp)
        {
            updatedTimestamp = previousTimestamp;

            IList<IListBlobItem> currentBlobs;

            try
            {
                currentBlobs = container.ListBlobs(useFlatBlobListing: true).ToList();
            }
            catch (StorageException exception)
            {
                if (exception.IsNotFound())
                {
                    return Enumerable.Empty<ICloudBlob>();
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
                if (cancellationToken.IsCancellationRequested)
                {
                    return Enumerable.Empty<ICloudBlob>();
                }

                try
                {
                    currentBlob.FetchAttributes();
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

            return newBlobs;
        }
    }
}
