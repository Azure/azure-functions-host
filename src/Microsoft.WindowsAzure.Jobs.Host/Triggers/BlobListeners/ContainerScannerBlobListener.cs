using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.WindowsAzure.Jobs
{
    // Full scan a container 
    // Uses a naive full-scanning algorithm. Easy, but very inefficient and does not scale to large containers.
    // But it is very deterministic. 
    internal class ContainerScannerBlobListener : IBlobListener
    {
        // Containers to listen to, and timestamp of last poll
        // Use parallel arrays instead of dicts to allow update during enumeration
        CloudBlobContainer[] _containers;
        DateTime[] _lastUpdateTimes;

        public ContainerScannerBlobListener(IEnumerable<CloudBlobContainer> containers)
        {
            DateTime start = new DateTime(1900, 1, 1); // old 

            _containers = containers.ToArray();

            _lastUpdateTimes = new DateTime[_containers.Length];
            for (int i = 0; i < _lastUpdateTimes.Length; i++)
            {
                _lastUpdateTimes[i] = start;
            }
        }

        // Does one iteration and then returns.
        // Poll containers, invoke callback for any new ones added.
        // $$$ Switch to queuing instead of just invoking callback
        public void Poll(Action<ICloudBlob, CancellationToken> callback, CancellationToken cancel)
        {
            for (int i = 0; !cancel.IsCancellationRequested && i < _containers.Length; i++)
            {
                var time = _lastUpdateTimes[i];

                var newBlobs = PollNewBlobs(_containers[i], time, cancel, ref _lastUpdateTimes[i]);

                foreach (var blob in newBlobs)
                {
                    callback(blob, cancel);
                }
            }
        }

        // lastScanTime - updated to the latest time in the container. Never call DateTime.Now because we need to avoid clock schewing problems. 
        public static IEnumerable<ICloudBlob> PollNewBlobs(CloudBlobContainer container, DateTime timestamp, CancellationToken cancel, ref DateTime lastScanTime)
        {
            List<ICloudBlob> blobs = new List<ICloudBlob>();

            try
            {
                container.CreateIfNotExists();
            }
            catch (StorageException)
            {
                // Can happen if container was deleted (or being deleted). Just ignore and return empty collection.
                // If it's deleted, there's nothing to listen for.
                return blobs;
            }

            foreach (var blobItem in container.ListBlobs(useFlatBlobListing: true))
            {
                if (cancel.IsCancellationRequested)
                {
                    return new ICloudBlob[0];
                }

                ICloudBlob b = container.GetBlobReferenceFromServer(blobItem.Uri.ToString());

                try
                {
                    b.FetchAttributes();
                }
                catch
                {
                    // Blob was likely deleted.
                    continue;
                }

                var props = b.Properties;
                var time = props.LastModified.Value.UtcDateTime;

                if (time > lastScanTime)
                {
                    lastScanTime = time;
                }

                if (time > timestamp)
                {
                    blobs.Add(b);
                }
            }

            return blobs;
        }
    }
}
