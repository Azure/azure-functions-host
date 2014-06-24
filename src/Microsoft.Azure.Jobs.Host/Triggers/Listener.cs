using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Blobs.Triggers;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Queue;

namespace Microsoft.Azure.Jobs
{
    // Listens on the triggers and invokes them when they fire
    internal class Listener
    {
        private readonly ITriggerInvoke _invoker;

        private Dictionary<CloudBlobContainer, List<BlobTrigger>> _map;
        private readonly IList<IntervalSeparationTimer> _queueListeners = new List<IntervalSeparationTimer>();

        private IBlobListener _blobListener;

        private Action<RuntimeBindingProviderContext> startPollingServiceBus = _ => { };
        private Action<ServiceBusTrigger> mapServiceBusTrigger = _ => { };

        public Listener(ITriggerMap map, ITriggerInvoke invoker, Worker worker)
        {
            InitServiceBusListener(worker);
            _invoker = invoker;

            _map = new Dictionary<CloudBlobContainer, List<BlobTrigger>>(new CloudContainerComparer());

            foreach (var scope in map.GetScopes())
            {
                // $$$ Could also do throttling per-scope. 
                var triggers = map.GetTriggers(scope);
                AddTriggers(scope, triggers);
            }

            var keys = _map.Keys;

            // IF we're listening on DevStorage, use a full scanner, since DevStorage doesn't support log listening. 
            // This also gives us very deterministic behavior for unit tests. 
            bool includesDevStorage = (keys.Count > 0) && (keys.First().ServiceClient.Credentials.AccountName == "devstoreaccount1");

            if (includesDevStorage)
            {
                // Full scan, very deterministic. But doesn't scale.
                _blobListener = new ContainerScannerBlobListener(keys);
            }
            else
            {
                // highly scalable, non-deterministic, less responsive.
                _blobListener = new BlobListener(keys);
            }
        }

        private void InitServiceBusListener(Worker worker)
        {
            var type = ServiceBusExtensionTypeLoader.Get("Microsoft.Azure.Jobs.ServiceBus.Listeners.ServiceBusListener");
            if (type == null)
            {
                return;
            }

            var serviceBusListener = Activator.CreateInstance(type, new object[] { worker });

            var serviceBusPollMethod = type.GetMethod("StartPollingServiceBus", new Type[] { typeof(RuntimeBindingProviderContext) });
            startPollingServiceBus = context => serviceBusPollMethod.Invoke(serviceBusListener, new object[] { context });

            var serviceBusMapMethod = type.GetMethod("Map", new Type[] { typeof(ServiceBusTrigger) });
            mapServiceBusTrigger = trigger => serviceBusMapMethod.Invoke(serviceBusListener, new object[] { trigger });
        }

        private void AddTriggers(string scope, Trigger[] funcs)
        {
            foreach (Trigger func in funcs)
            {
                var blobTrigger = func as BlobTrigger;
                if (blobTrigger != null)
                {
                    IBlobPathSource path = blobTrigger.BlobInput;

                    CloudStorageAccount account = GetAccount(scope, func);
                    CloudBlobClient clientBlob = account.CreateCloudBlobClient();
                    string containerName = path.ContainerNamePattern;
                    CloudBlobContainer container = clientBlob.GetContainerReference(containerName);

                    _map.GetOrCreate(container).Add(blobTrigger);
                }

                var serviceBusTrigger = func as ServiceBusTrigger;
                mapServiceBusTrigger(serviceBusTrigger);
            }
        }

        // $$$ Should get account via Azure Web Sites internal api.  Abstract this out. 
        private CloudStorageAccount GetAccount(string scope, Trigger func)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(func.StorageConnectionString);
            return account;
        }

        public void Poll(RuntimeBindingProviderContext context)
        {
            try
            {
                PollBlobs(context);
            }
            catch (StorageException)
            {
                // Storage exceptions can happen naturally and intermittently from network connectivity issues. Just ignore. 
            }
        }

        public void StartPolling(RuntimeBindingProviderContext context)
        {
            startPollingServiceBus(context);
        }

        private void PollBlobs(RuntimeBindingProviderContext context)
        {
            _blobListener.Poll(OnNewBlobWorker, context);
        }

        // Called as a hint if an external source knows we have a new blob. Will invoke triggers. 
        // This will invoke back any associated triggers
        public void InvokeTriggersForBlob(string accountName, string containerName, string blobName, RuntimeBindingProviderContext context)
        {
            foreach (var container in _map.Keys)
            {
                if (containerName == container.Name) // names must be lowercase, so technically case-sensitive
                {
                    bool sameAccount = String.Equals(container.ServiceClient.Credentials.AccountName, accountName, StringComparison.OrdinalIgnoreCase);
                    if (sameAccount)
                    {
                        var blob = container.GetBlockBlobReference(blobName);
                        OnNewBlobWorker(blob, context);
                    }
                }
            }
        }

        private void OnNewBlobWorker(ICloudBlob blob, RuntimeBindingProviderContext context)
        {
            var client = blob.ServiceClient;

            var container = blob.Container;

            DateTime inputTime;
            {
                var inputTimeCheck = BlobClient.GetBlobModifiedUtcTime(blob);
                if (!inputTimeCheck.HasValue)
                {
                    // Shouldn't happen. This means blob is missing, but we were called because blob was discovered.
                    // did somebody delete it on us?
                    return;
                }
                inputTime = inputTimeCheck.Value;
            }

            Func<BlobPath, DateTime?> fpGetModifiedTime = path => GetModifiedTime(client, path);

            List<BlobTrigger> list;
            if (_map.TryGetValue(container, out list))
            {
                // Invoke all these functions
                foreach (BlobTrigger func in list)
                {
                    IBlobPathSource blobPathPattern = func.BlobInput;
                    var nvc = blobPathPattern.CreateBindingData(blob.ToBlobPath());

                    if (nvc == null)
                    {
                        continue; // Didn't match
                    }

                    // If yes, do output test. 
                    // - rerun if any outputs are missing 
                    // - or if Time(input) is more recent than any Time(Output)

                    bool invoke = ShouldInvokeTrigger(func, nvc, inputTime, fpGetModifiedTime);

                    if (invoke)
                    {
                        _invoker.OnNewBlob(blob, func, context);
                    }
                }
            }
        }

        // Helper to get the last modified time for a given (resolved) blob path. 
        private static DateTime? GetModifiedTime(CloudBlobClient client, BlobPath path)
        {
            var container = client.GetContainerReference(path.ContainerName);
            var blob = container.GetBlockBlobReference(path.BlobName);

            var time = BlobClient.GetBlobModifiedUtcTime(blob);
            return time;
        }

        // Return true if we should invoke the blob trigger. Called when a corresponding input is detected.
        // trigger - the blob trigger to evaluate. 
        // nvc - route parameters from the input blob. These are used to resolve the output blobs
        // inputTime - last modified time for the input blob
        // fpGetModifiedTime - function to resolve times of the outputs (returns null if no output found)
        public static bool ShouldInvokeTrigger(BlobTrigger trigger, IReadOnlyDictionary<string, object> nvc, DateTime inputTime, Func<BlobPath, DateTime?> fpGetModifiedTime)
        {
            if (trigger.BlobOutputs == null)
            {
                return true;
            }
            if (trigger.BlobOutputs.Length == 0)
            {
                return true;
            }

            foreach (var outputPath in trigger.BlobOutputs)
            {
                BlobPath outputPathResolved = outputPath.Bind(nvc);

                var outputTime = fpGetModifiedTime(outputPathResolved);

                if (!outputTime.HasValue)
                {
                    // Output is missing. Need to rerun. 
                    return true;
                }
                if (inputTime > outputTime.Value)
                {
                    // input time is more recent. Outputs are stale, rerun. 
                    return true;
                }
            }
            return false;
        }
    }
}
