using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Executor;
using Microsoft.WindowsAzure;
using Microsoft.WindowsAzure.StorageClient;
using RunnerHost;
using RunnerInterfaces;

namespace Orchestrator
{
    public class OrchestratorRoleHeartbeat
    {
        public DateTime Uptime { get; set; } // when this node went up
        public DateTime LastCacheReset { get; set; } // when were the caches last reset
        public DateTime Heartbeat { get; set; } // last scan time

        // ??? Add something about progress through listening on a large container?
    }

    public class Worker : IDisposable
    {
        OrchestratorRoleHeartbeat _heartbeat = new OrchestratorRoleHeartbeat();

        // When we notice the input is added, invoke this function 
        // String hashes on CloubBlobContainer
        Dictionary<CloudBlobContainer, List<FunctionIndexEntity>> _map;
        Dictionary<CloudQueue, List<FunctionIndexEntity>> _mapQueues;
        private BlobListener _blobListener;
        
        // Settings is for wiring up Azure endpoints for the distributed app.
        private readonly IFunctionTable _functionTable;
        private readonly IQueueFunction _execute;

        public Worker(IFunctionTable functionTable, IQueueFunction execute)
        {
            if (functionTable == null)
            {
                throw new ArgumentNullException("functionTable");
            }
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }
            _functionTable = functionTable;
            _execute = execute;

            _map = new Dictionary<CloudBlobContainer, List<FunctionIndexEntity>>(new CloudContainerComparer());
            _mapQueues = new Dictionary<CloudQueue, List<FunctionIndexEntity>>(new CloudQueueComparer());

            CreateInputMap();

            this._heartbeat.LastCacheReset = DateTime.UtcNow;
        }

        public OrchestratorRoleHeartbeat Heartbeat
        {
            get { return _heartbeat; }
        }

        // Called once at startup to initialize orchestration data structures
        // This is just retrieving the data structures created by the Indexer.
        private void CreateInputMap()
        {
            FunctionIndexEntity[] funcs = _functionTable.ReadAll();

            CreateInputMap(funcs);
        }

        // List of functions to execute. 
        // May have been triggered by a timer on a background thread . 
        // Process by main foreground thread. 
        volatile ConcurrentQueue<FunctionIndexEntity> _queueExecuteFuncs;

        List<Timer> _timers = new List<Timer>();

        void DisposeTimers()
        {
            foreach (var timer in _timers)
            {
                timer.Dispose();
            }
            _timers.Clear();
        }

        private void CreateInputMap(FunctionIndexEntity[] funcs)
        {
            _queueExecuteFuncs = new ConcurrentQueue<FunctionIndexEntity>();
            foreach (FunctionIndexEntity func in funcs)
            {
                var trigger = func.Trigger;

                if (trigger.GetTimerInterval().HasValue)
                {
                    TimeSpan period = trigger.GetTimerInterval().Value;

                    Timer timer = null;
                    TimerCallback callback = obj => 
                    {
                        // Called back on an arbitrary thread.                        
                        if (_queueExecuteFuncs != null)                        
                        {
                            _queueExecuteFuncs.Enqueue(func);
                        }
                    };

                    timer = new Timer(callback, null, TimeSpan.FromMinutes(0), period);
                    _timers.Add(timer);
                    
                    // Don't listen on any other inputs.
                    continue;
                }

                var flow = func.Flow;
                foreach (var input in flow.Bindings)
                {
                    if (trigger.ListenOnBlobs)
                    {
                        var blobBinding = input as BlobParameterStaticBinding;
                        if (blobBinding != null)
                        {
                            if (!blobBinding.IsInput)
                            {
                                continue;
                            }
                            CloudBlobPath path = blobBinding.Path;
                            string containerName = path.ContainerName;

                            // Check if it's on the ignore list
                            var account = func.GetAccount();

                            bool ignore = false;
                            string accountContainerName = account.Credentials.AccountName + "\\" + containerName;

                            if (!ignore)
                            {
                                CloudBlobClient clientBlob = account.CreateCloudBlobClient();
                                CloudBlobContainer container = clientBlob.GetContainerReference(containerName);

                                _map.GetOrCreate(container).Add(func);
                            }

                            // $$$ Policy: only listen on the the first input 
                            break;
                        }
                    }

                    var queueBinding = input as QueueParameterStaticBinding;
                    if (queueBinding != null)
                    {
                        if (queueBinding.IsInput)
                        {
                            // Queuenames must be all lowercase. Normalize for convenience. 
                            string queueName = queueBinding.QueueName.ToLower();

                            CloudQueueClient clientQueue = func.GetAccount().CreateCloudQueueClient();
                            CloudQueue queue = clientQueue.GetQueueReference(queueName);

                            _mapQueues.GetOrCreate(queue).Add(func);
                            break;
                        }
                    }
                }
            }

            _blobListener = new BlobListener(_map.Keys);
        }


        public void Run()
        {
            while (true)
            {
                Poll(CancellationToken.None);

                // This sleep is a propagation delay for wiring an output up to the next thing to run. 
                // Could optimize that away by analyzing outputs and determining if we should immediately execute a new func.
                Thread.Sleep(2 * 1000);
            }
        }

        // Does one iteration
        // Polling can take a long time, so pass in a cancellation token to allow aborting. 
        public void Poll()
        {
            Poll(CancellationToken.None);
        }

        public void Poll(CancellationToken cancel)
        {
            _heartbeat.Heartbeat = DateTime.UtcNow;
            _blobListener.Poll(OnNewBlobMaybe, cancel);

            PollTimers();

            PollQueues();
        }

        private void PollTimers()
        {
            while (true)
            {
                FunctionIndexEntity func;
                if (!_queueExecuteFuncs.TryDequeue(out func))
                {
                    break;
                }

                var instance = GetFunctionInvocation(func);

                if (instance != null)
                {
                    instance.TriggerReason = new TimerTriggerReason();                         
                    _execute.Queue(instance);
                }
            }
        }

        // Listen for all queue results.
        private void PollQueues()
        {
            foreach (var kv in _mapQueues)
            {
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
                    foreach (FunctionIndexEntity func in funcs)
                    {
                        OnNewQueueItem(msg, func);
                        
                        // $$$ Need to call Delete message only if function succeeded. 
                        // and that gets trickier when we have multiple funcs listening. 
                        queue.DeleteMessage(msg);
                    }
                }
            }
        }

        private void OnNewQueueItem(CloudQueueMessage msg, FunctionIndexEntity func)
        {
            string payload = msg.AsString;

            // blobInput was the one that triggered it.            

            RuntimeBindingInputs ctx = new NewQueueMessageRuntimeBindingInputs(func.Location, msg);            

            var instance = BindParameters(ctx, func);
            
            if (instance != null)
            {
                instance.TriggerReason = new QueueMessageTriggerReason
                {
                    MessageId = msg.Id,
                    ParentGuid = GetOwnerFromMessage(msg)
                };
                _execute.Queue(instance);
            }
        }

        // Supports explicitly invoking any functions associated with this blob. 
        public void OnNewBlob(CloudBlob blob)
        {
            OnNewBlobWorker(blob, alwaysRun: true);
        }

        // Run the blob, only if the inputs are newer than the outputs.
        private void OnNewBlobMaybe(CloudBlob blob)
        {
            OnNewBlobWorker(blob, alwaysRun: false);
        }

        private void OnNewBlobWorker(CloudBlob blob, bool alwaysRun)
        {
            var container = blob.Container;
            Console.WriteLine(@"# New blob: {0}", blob.Uri);

            List<FunctionIndexEntity> list;
            if (_map.TryGetValue(container, out list))
            {
                // Invoke all these functions
                foreach (FunctionIndexEntity func in list)
                {
                    FunctionInvokeRequest instance = GetFunctionInvocation(func, blob);
                    if (instance != null)
                    {
                        if (!alwaysRun)
                        {
                            // Allow for optimization to skip execution if outputs are all newer than inputs.
                            var inputsAreNewerThanOutputs = CheckBlobTimes(instance, func);
                            if (inputsAreNewerThanOutputs.HasValue)
                            {
                                if (!inputsAreNewerThanOutputs.Value)
                                {
                                    continue;
                                }
                            }
                        }

                        Guid parentGuid = GetBlobWriterGuid(blob);
                        instance.TriggerReason = new BlobTriggerReason
                        {
                            BlobPath = new CloudBlobPath(blob),
                            ParentGuid = parentGuid
                        };                      

                        _execute.Queue(instance);
                    }
                }
            }
        }

        private Guid GetBlobWriterGuid(CloudBlob blob)
        {
            IBlobCausalityLogger logger = new BlobCausalityLogger();
            return logger.GetWriter(blob);
        }

        private Guid GetOwnerFromMessage(CloudQueueMessage msg)
        {
            return Guid.Empty; // !!! Fill it out
        }


        // If function is just blob input and output, and the output blobs are newe, then don't rerun.
        // This is a key optimization for avoiding expensive operations.
        // Pass in func to save cost of having to look it up again.
        // Return null if we can't reason about it.
        // Return True is inputs are newer than outputs, and so function should be executed again.
        // Else return false (meaning function execution can be skipped)
        public static bool? CheckBlobTimes(FunctionInvokeRequest instance, FunctionIndexEntity func)
        {
            return CheckBlobTimes(instance.Args, func.Flow.Bindings);
        }

        // Easier exposure for unit testing
        public static bool? CheckBlobTimes(ParameterRuntimeBinding[] argsRuntime, ParameterStaticBinding[] flows)
        {
            if (argsRuntime.Length != flows.Length)
            {
                throw new ArgumentException("arrays should be same length");
            }

            DateTime newestInput = DateTime.MinValue; // Old
            DateTime oldestOutput = DateTime.MaxValue; // New

            for (int i = 0; i < argsRuntime.Length; i++)            
            {
                var arg = argsRuntime[i];
                var flow = flows[i];

                // Input, Output, Ignore, Unknown

                var t = flow.GetTriggerType();

                if (t == TriggerType.Ignore)
                {
                    continue;
                }
                

                DateTime? time = arg.LastModifiedTime;

                if (t == TriggerType.Input)
                {
                    if (!time.HasValue)
                    {
                        // Missing an input? We shouldn't even be trying to invoke this function then.
                        // Skip optimizations and let the binder deal with it. Binder should issue a nice error message.
                        return null;
                    }
                    if (time > newestInput)
                    {
                        newestInput = time.Value;
                    }
                }
                else
                {
                    if (!time.HasValue)
                    {
                        // No output. This is a common case.
                        // Run the function to generate it.
                        return true;
                    }
                    if (time < oldestOutput)
                    {
                        oldestOutput = time.Value;
                    }
                }                
            }

            if (newestInput == DateTime.MinValue)
            {
                return null;
            }

            if (oldestOutput == DateTime.MaxValue)
            {
                // No outputs. So question is moot.
                return null;
            }

            // All inputs and outputs are already present. 
            bool inputsAreNewerThanOutputs = (newestInput > oldestOutput);
            return inputsAreNewerThanOutputs;
        }

        public static FunctionInvokeRequest GetFunctionInvocation(FunctionIndexEntity func, IDictionary<string, string> parameters)
        {
            var ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = parameters
            };
            var instance = BindParameters(ctx, func);
            return instance;
        }

        // Invoke a function that is completely self-describing.
        // This means all inputs can be bound without any additional information. 
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionIndexEntity func)
        {
            var ctx = new RuntimeBindingInputs(func.Location);
            var instance = BindParameters(ctx, func);
            return instance;
        }


        // policy: blobInput is the first [Input] attribute. Functions are triggered by single input.
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionIndexEntity func, CloudBlob blobInput)
        {
            // blobInput was the one that triggered it.
            // Get the path from the first blob input parameter.
            var flow = func.Flow;
            CloudBlobPath firstInput = flow.Bindings.OfType<BlobParameterStaticBinding>().Where(b => b.IsInput).Select(b=>b.Path).FirstOrDefault();
                        
            var p = firstInput.Match(new CloudBlobPath(blobInput));
            if (p == null)
            {
                // No match.
                return null;
            }

            var ctx = new NewBlobRuntimeBindingInputs(func.Location, blobInput)
            {
                NameParameters = p,
            };
            return BindParameters(ctx, func);
        }

        public void Dispose()
        {
            DisposeTimers();
        }

        // Bind the entire flow to an instance
        public static FunctionInvokeRequest BindParameters(RuntimeBindingInputs ctx, FunctionIndexEntity func)
        {
            FunctionFlow flow = func.Flow;
            int len = flow.Bindings.Length;

            var args = Array.ConvertAll(flow.Bindings, staticBinding => staticBinding.Bind(ctx));

            FunctionInvokeRequest instance = new FunctionInvokeRequest
            {
                Location = func.Location,
                Args = args
            };
            return instance;
        }
    }
}