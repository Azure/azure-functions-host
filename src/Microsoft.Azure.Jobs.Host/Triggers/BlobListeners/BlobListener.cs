using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs
{
    // Hybrid blob listening. 
    // Will do a full scan of all containers on startup 
    // And then listens for logs in steady-state. Note that log listening can have a 10+ minute delay. 
    // Not deterministic when the blobs show up in the logs. 
    internal class BlobListener : IBlobListener
    {
        CloudBlobContainer[] _containers;
        BlobLogListener[] _clientListeners; // for log listening

        // Union of container names across all clients. For fast filtering. 
        // $$$ could optimize this by making it client specific. 
        HashSet<string> _containerNames = new HashSet<string>();

        bool _completedFullScanOnStartup;
        CancellationToken _backgroundCancel; // cancellation token for the full scan running in the background

        public BlobListener(IEnumerable<CloudBlobContainer> containers)
        {
            _containers = containers.ToArray();

            // Get unique set of clients for the containers. 
            var clients = new HashSet<CloudBlobClient>(new CloudBlobClientComparer());
            foreach (var container in containers)
            {
                _containerNames.Add(container.Name);
                clients.Add(container.ServiceClient);
            }


            List<BlobLogListener> x = new List<BlobLogListener>();

            foreach (var client in clients)
            {
                try
                {
                    var ll = new BlobLogListener(client);
                    x.Add(ll);
                }
                catch
                {
                    // Likely that client credentials were not valid. 
                }
            }

            _clientListeners = x.ToArray();
        }

        // Need to do a full scan on startup 
        // This can take a while so kick off on background thread and queues results to main thread?        
        void InitialScan()
        {
            // Include cancellation

            foreach (var container in _containers)
            {
                Thread t = new Thread(_ => FullScanContainer(container));
                t.Start();
            }
        }

        ConcurrentQueue<ICloudBlob> _queueExistingBlobs = new ConcurrentQueue<ICloudBlob>();

        private void FullScanContainer(CloudBlobContainer container)
        {
            try
            {
                container.CreateIfNotExists();
            }
            catch (StorageException)
            {
                // Can happen if container was deleted (or being deleted). Just ignore and return empty collection.
                // If it's deleted, there's nothing to listen for.           
                return;
            }

            foreach (ICloudBlob blobItem in container.ListBlobs(useFlatBlobListing: true))
            {
                if (_backgroundCancel.IsCancellationRequested)
                {
                    return;
                }

                _queueExistingBlobs.Enqueue(blobItem);
            }
        }


        // Blob could have been deleted by the time the callback is invoked. 
        // - race where it was explicitly deleted
        // - if we detected blob via a log, then there's a long window (possibly hours) where it could have easily been deleted. 
        public void Poll(Action<ICloudBlob, RuntimeBindingProviderContext> callback, RuntimeBindingProviderContext context)
        {
            if (!_completedFullScanOnStartup)
            {
                _backgroundCancel = context.CancellationToken;
                _completedFullScanOnStartup = true;
                InitialScan();
            }

            // Drain the background queues. 
            while (true)
            {
                ICloudBlob b;
                if (!_queueExistingBlobs.TryDequeue(out b))
                {
                    break;
                }
                callback(b, context);
            }

            // Listen on logs for new events. 
            foreach (var client in _clientListeners)
            {
                if (context.CancellationToken.IsCancellationRequested)
                {
                    return;
                }

                foreach (var path in client.GetRecentBlobWrites())
                {
                    if (context.CancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    // Log listening is Client wide. So filter out things that aren't in the container list. 
                    if (!_containerNames.Contains(path.ContainerName))
                    {
                        continue;
                    }

                    var blob = path.Resolve(client.Client);

                    callback(blob, context);
                }
            }
        }
    }
}
