using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using TriggerService.Internal;

namespace TriggerService
{
    // Listens on the triggers and invokes them when they fire
    public class Listener : IDisposable
    {
        private readonly ITriggerInvoke _invoker;

        private Dictionary<CloudBlobContainer, List<BlobTrigger>> _map;
        private Dictionary<CloudQueue, List<QueueTrigger>> _mapQueues;

        private IBlobListener _blobListener;

        // List of functions to execute. 
        // May have been triggered by a timer on a background thread . 
        // Process by main foreground thread. 
        volatile ConcurrentQueue<TimerTrigger> _queueExecuteFuncs;

        List<Timer> _timers;

        public Listener(ITriggerMap map, ITriggerInvoke invoker)
        {
            _invoker = invoker;

            _queueExecuteFuncs = new ConcurrentQueue<TimerTrigger>();
            _map = new Dictionary<CloudBlobContainer, List<BlobTrigger>>(new CloudContainerComparer());
            _mapQueues = new Dictionary<CloudQueue, List<QueueTrigger>>(new CloudQueueComparer());
            _timers = new List<Timer>();

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

        private void DisposeTimers()
        {
            foreach (var timer in _timers)
            {
                timer.Dispose();
            }
            _timers.Clear();
        }

        private void AddTriggers(string scope, Trigger[] funcs)
        {
            foreach (Trigger func in funcs)
            {
                var blobTrigger = func as BlobTrigger;
                if (blobTrigger != null)
                {
                    CloudBlobPath path = blobTrigger.BlobInput;

                    CloudStorageAccount account = GetAccount(scope, func);
                    CloudBlobClient clientBlob = account.CreateCloudBlobClient();
                    string containerName = path.ContainerName;
                    CloudBlobContainer container = clientBlob.GetContainerReference(containerName);

                    _map.GetOrCreate(container).Add(blobTrigger);
                }

                var timerTrigger = func as TimerTrigger;
                if (timerTrigger != null)
                {
                    TimeSpan period = timerTrigger.Interval;
                    Timer timer = null;
                    TimerCallback callback = obj =>
                    {
                        // Called back on an arbitrary thread.                        
                        if (_queueExecuteFuncs != null)
                        {
                            _queueExecuteFuncs.Enqueue(timerTrigger);
                        }
                    };

                    timer = new Timer(callback, null, TimeSpan.FromMinutes(0), period);
                    _timers.Add(timer);                    
                }


                var queueTrigger = func as QueueTrigger;
                if (queueTrigger != null)
                {
                    CloudStorageAccount account = GetAccount(scope, func);
                    CloudQueueClient clientQueue = account.CreateCloudQueueClient();

                    // Queuenames must be all lowercase. Normalize for convenience. 
                    string queueName = queueTrigger.QueueName;

                    CloudQueue queue = clientQueue.GetQueueReference(queueName);

                    _mapQueues.GetOrCreate(queue).Add(queueTrigger);
                }
            }
        }

        // $$$ Should get account via Antares internal api.  Abstract this out. 
        private CloudStorageAccount GetAccount(string scope, Trigger func)
        {
            CloudStorageAccount account = CloudStorageAccount.Parse(func.AccountConnectionString);
            return account;
        }


        public void Poll()
        {
            Poll(CancellationToken.None);
        }
        public void Poll(CancellationToken token)
        {
            try
            {
                PollBlobs(token);
                PollQueues(token);
            }
            catch (StorageException)
            {
                // Storage exceptions can happen naturally and intermittently from network connectivity issues. Just ignore. 
            }
            PollTimers(token);
        }

        private void PollBlobs(CancellationToken token)
        {
            _blobListener.Poll(OnNewBlobWorker, token);
        }

        // Listen on timers
        private void PollTimers(CancellationToken token)
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();

                TimerTrigger func;
                if (!_queueExecuteFuncs.TryDequeue(out func))
                {
                    break;
                }

                _invoker.OnNewTimer(func, token);                
            }
        }

        // Called as a hint if an external source knows we have a new blob. Will invoke triggers. 
        // This will invoke back any associated triggers
        public void InvokeTriggersForBlob(string accountName, string containerName, string blobName)
        {
            foreach (var container in _map.Keys)
            {
                if (containerName == container.Name) // names must be lowercase, so technically case-sensitive
                {
                    bool sameAccount = string.Compare(container.ServiceClient.Credentials.AccountName, accountName, ignoreCase: true) == 0;
                    if (sameAccount)
                    {
                        var blob = container.GetBlobReference(blobName);
                        OnNewBlobWorker(blob);                        
                    }
                }
            }
        }

        private void OnNewBlobWorker(CloudBlob blob)
        {
            var client = blob.ServiceClient;

            var blobPathActual = new CloudBlobPath(blob);
            var container = blob.Container;

            DateTime inputTime;
            {
                var inputTimeCheck = Utility.GetBlobModifiedUtcTime(blob);
                if (!inputTimeCheck.HasValue)
                {
                    // Shouldn't happen. This means blob is missing, but we were called because blob was discovered.
                    // did somebody delete it on us?
                    return;
                }
                inputTime = inputTimeCheck.Value;
            }

            Func<CloudBlobPath, DateTime?> fpGetModifiedTime = path => GetModifiedTime(client, path);

            List<BlobTrigger> list;
            if (_map.TryGetValue(container, out list))
            {
                // Invoke all these functions
                foreach (BlobTrigger func in list)
                {
                    CloudBlobPath blobPathPattern = func.BlobInput;
                    var nvc = blobPathPattern.Match(blobPathActual);

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
                        _invoker.OnNewBlob(blob, func, CancellationToken.None);
                    }
                }
            }
        }

        // Helper to get the last modified time for a given (resolved) blob path. 
        private static DateTime? GetModifiedTime(CloudBlobClient client, CloudBlobPath path)
        {
            var container = client.GetContainerReference(path.ContainerName);
            var blob = container.GetBlobReference(path.BlobName);

            var time = Utility.GetBlobModifiedUtcTime(blob);
            return time;
        }


        // Return true if we should invoke the blob trigger. Called when a corresponding input is detected.
        // trigger - the blob trigger to evaluate. 
        // nvc - route parameters from the input blob. These are used to resolve the output blobs
        // inputTime - last modified time for the input blob
        // fpGetModifiedTime - function to resolve times of the outputs (returns null if no output found)
        public static bool ShouldInvokeTrigger(BlobTrigger trigger, IDictionary<string, string> nvc, DateTime inputTime, Func<CloudBlobPath, DateTime?> fpGetModifiedTime)
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
                CloudBlobPath outputPathResolved = outputPath.ApplyNames(nvc);

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
                        
        // Listen for all queue results.
        private void PollQueues(CancellationToken token)
        {
            foreach (var kv in _mapQueues)
            {
                token.ThrowIfCancellationRequested();

                var queue = kv.Key;
                var funcs = kv.Value;

                if (!queue.Exists())
                {
                    continue;
                }

                // What if job takes longer. Call CloudQueue.UpdateMessage
                var visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
                var msg = queue.GetMessage(visibilityTimeout);
                if (msg != null)
                {
                    foreach (var func in funcs)
                    {
                        token.ThrowIfCancellationRequested();

                        _invoker.OnNewQueueItem(msg, func, token);
                    }

                    // Need to call Delete message only if function succeeded. 
                    // and that gets trickier when we have multiple funcs listening. 
                    queue.DeleteMessage(msg);
                }
            }
        }

        public void Dispose()
        {
            this.DisposeTimers();
        }
    }
}
