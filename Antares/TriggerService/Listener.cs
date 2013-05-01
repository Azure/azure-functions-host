using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;

namespace TriggerService
{
    public class WebInvoke : ITriggerInvoke
    {
        public void OnNewTimer(TimerTrigger func, CancellationToken token)
        {
            Post(func.CallbackPath, token);
        }

        public void OnNewBlob(CloudBlob blob, BlobTrigger func, CancellationToken token)
        {
            // $$$ Have to provide the blob name. Do it as NVC?
            Post(func.CallbackPath, token);
        }

        public void OnNewQueueItem(CloudQueueMessage msg, QueueTrigger func, CancellationToken token)
        {
            byte[] contents = msg.AsBytes;
            Post(func.CallbackPath, token, contents);
        }

        private void Post(string url, CancellationToken token, byte[] contents = null)
        {
            if (contents == null)
            {
                contents = new byte[0];
            }

            try
            {
                HttpClient c = new HttpClient();
                var result = c.PostAsync(url, new ByteArrayContent(contents), token);
                HttpResponseMessage response = result.Result;
            }
            catch
            {
                // $$$ What ot do about errors? Bad user URL?
            }
        }
    }

    public class Listener : IDisposable
    {
        private readonly ITriggerInvoke _invoker;

        private Dictionary<CloudBlobContainer, List<BlobTrigger>> _map;
        private Dictionary<CloudQueue, List<QueueTrigger>> _mapQueues;

        private BlobListener _blobListener;

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

            _blobListener = new BlobListener(_map.Keys);
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
                    CloudBlobPath path = new CloudBlobPath(blobTrigger.BlobInput);
                    string containerName = path.ContainerName;
                    CloudStorageAccount account = GetAccount(scope, func);

                    CloudBlobClient clientBlob = account.CreateCloudBlobClient();
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
            PollBlobs(token);
            PollQueues(token);
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


        private void OnNewBlobWorker(CloudBlob blob)
        {
            var client = blob.ServiceClient;

            var container = blob.Container;

            List<BlobTrigger> list;
            if (_map.TryGetValue(container, out list))
            {
                // Invoke all these functions
                foreach (BlobTrigger func in list)
                {
                    // !!! Does it match input?

                    CloudBlobPath firstInput = new CloudBlobPath(func.BlobInput);                    

                    var p = firstInput.Match(new CloudBlobPath(blob));

                    if (p == null)
                    {
                        continue; // Didn't match
                    }

                    // If yes, do output test. 
                    // - rerun if any outputs are missing 
                    // - or if Time(input) is more recent than any Time(Output)

                    bool invoke = true;
                    if (func.BlobOutput != null)
                    {
                        var inputTime = Utility.GetBlobModifiedUtcTime(blob);
                        invoke = ShouldInvokeTrigger(client, func, p, inputTime);
                    }

                    if (invoke)
                    {
                        _invoker.OnNewBlob(blob, func, CancellationToken.None);
                    }
                }
            }
        }

        // Return true if we should invoke the blob trigger
        private static bool ShouldInvokeTrigger(CloudBlobClient client, BlobTrigger func, IDictionary<string, string> p, DateTime? inputTime)
        {
            if (!inputTime.HasValue)
            {
                return false;
            }

            var outputs = func.BlobOutput.Split(';');
            foreach (var output in outputs)
            {
                CloudBlobPath outputPath = new CloudBlobPath(output);
                var outputPathResolved = outputPath.ApplyNames(p);

                var outputContainer = client.GetContainerReference(outputPathResolved.ContainerName);
                var outputBlob = outputContainer.GetBlobReference(outputPathResolved.BlobName);

                var outputTime = Utility.GetBlobModifiedUtcTime(outputBlob);
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


        private void CheckTime(CloudBlob input, CloudBlob output)
        {

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

                // $$$ What if job takes longer. Call CloudQueue.UpdateMessage
                var visibilityTimeout = TimeSpan.FromMinutes(10); // long enough to process the job
                var msg = queue.GetMessage(visibilityTimeout);
                if (msg != null)
                {
                    foreach (var func in funcs)
                    {
                        token.ThrowIfCancellationRequested();

                        _invoker.OnNewQueueItem(msg, func, token);

                        // $$$ Need to call Delete message only if function succeeded. 
                        // and that gets trickier when we have multiple funcs listening. 
                        queue.DeleteMessage(msg);
                    }
                }
            }
        }

        public void Dispose()
        {
            this.DisposeTimers();
        }
    }
}
