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
using TriggerService;
using System.Text;

namespace Orchestrator
{
    internal class OrchestratorRoleHeartbeat
    {
        public DateTime Uptime { get; set; } // when this node went up
        public DateTime LastCacheReset { get; set; } // when were the caches last reset
        public DateTime Heartbeat { get; set; } // last scan time

        // ??? Add something about progress through listening on a large container?
    }

    internal class Worker : IDisposable
    {
        OrchestratorRoleHeartbeat _heartbeat = new OrchestratorRoleHeartbeat();

        private readonly string _hostName;

        // Settings is for wiring up Azure endpoints for the distributed app.
        private readonly IFunctionTableLookup _functionTable;
        private readonly IRunningHostTableWriter _heartbeatTable;
        private readonly IQueueFunction _execute;

        // General purpose listener for blobs, queues. 
        private Listener _listener;

        // Fast-path blob listener. 
        private INotifyNewBlobListener _blobListener;

        private DateTime _nextHeartbeat;
        
        public Worker(string hostName, IFunctionTableLookup functionTable, IRunningHostTableWriter heartbeatTable, IQueueFunction execute, INotifyNewBlobListener blobListener = null)
        {
            _blobListener = blobListener;
            if (hostName == null)
            {
                throw new ArgumentNullException("hostName");
            }
            if (functionTable == null)
            {
                throw new ArgumentNullException("functionTable");
            }
            if (heartbeatTable == null)
            {
                throw new ArgumentNullException("heartbeatTable");
            }
            if (execute == null)
            {
                throw new ArgumentNullException("execute");
            }
            _hostName = hostName;
            _functionTable = functionTable;
            _heartbeatTable = heartbeatTable;
            _execute = execute;

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
            FunctionDefinition[] funcs = _functionTable.ReadAll();

            TriggerMap map = new TriggerMap();

            foreach (var func in funcs)
            {
                var ts = CalculateTriggers.GetTrigger(func);
                if (ts != null)
                {
                    map.AddTriggers(func.Location.GetId(), ts);
                }
            }

            _listener = new Listener(map, new MyInvoker(this));
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

        int _triggerCount = 0;

        public void Poll(CancellationToken token)
        {
            _heartbeat.Heartbeat = DateTime.UtcNow;
            
            if (_heartbeat.Heartbeat > _nextHeartbeat)
            {
                _heartbeatTable.SignalHeartbeat(_hostName);
                _nextHeartbeat = DateTime.UtcNow.Add(RunningHost.HeartbeatSignalInterval);
            }

            while (true)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                int lastCount = _triggerCount;

                // this is a fast poll (checking a queue), so give it high priority
                PollNotifyNewBlobs(token);
                if (_triggerCount != lastCount)
                {
                    // This is a critical optimization.
                    // If a function writes a blob, immediately execute any functions that would
                    // have been triggered by that blob. Don't wait for a slow blob polling to detect it. 
                    continue;
                }

                _listener.Poll(token);

                if (_triggerCount != lastCount)
                {
                    continue;
                }
                break;
            }            
        }

        // Poll blob notifications from the fast path that may be detected ahead of our
        // normal listeners. 
        void PollNotifyNewBlobs(CancellationToken token)
        {
            if (_blobListener != null)
            {
                _blobListener.ProcessMessages(this.NewBlob, token);
            }
        }

        // Called if the external system thinks we may have a new blob. 
        public void NewBlob(BlobWrittenMessage msg)
        {
            _listener.InvokeTriggersForBlob(msg.AccountName, msg.ContainerName, msg.BlobName);
        }

        private void OnNewTimer(FunctionDefinition func)
        {
            var instance = GetFunctionInvocation(func);

            if (instance != null)
            {
                _triggerCount++;
                instance.TriggerReason = new TimerTriggerReason();
                _execute.Queue(instance);
            }
        }

        private void OnNewQueueItem(CloudQueueMessage msg, FunctionDefinition func)
        {
            var instance = GetFunctionInvocation(func, msg);
            
            if (instance != null)
            {
                _triggerCount++;
                _execute.Queue(instance);
            }
        }

        // Supports explicitly invoking any functions associated with this blob. 
        private void OnNewBlob(FunctionDefinition func, CloudBlob blob)
        {
            FunctionInvokeRequest instance = GetFunctionInvocation(func, blob);
            if (instance != null)
            {
                _triggerCount++;
                _execute.Queue(instance);
            }
        }

        private static Guid GetBlobWriterGuid(CloudBlob blob)
        {
            IBlobCausalityLogger logger = new BlobCausalityLogger();
            return logger.GetWriter(blob);
        }

        private static Guid GetOwnerFromMessage(CloudQueueMessage msg)
        {
            QueueCausalityHelper qcm = new QueueCausalityHelper();
            return qcm.GetOwner(msg);
        }
        
        public static FunctionInvokeRequest GetFunctionInvocation(
            FunctionDefinition func, 
            IDictionary<string, string> parameters,
            IEnumerable<Guid> prereqs = null)
        {
            var ctx = new RuntimeBindingInputs(func.Location)
            {
                NameParameters = parameters
            };
            var instance = BindParameters(ctx, func);

            if (prereqs != null && prereqs.Any())
            {
                instance.Prereqs = prereqs.ToArray();
            }

            return instance;
        }

        // Invoke a function that is completely self-describing.
        // This means all inputs can be bound without any additional information. 
        // No reason set. 
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func)
        {
            var ctx = new RuntimeBindingInputs(func.Location);
            var instance = BindParameters(ctx, func);
            return instance;
        }


        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, CloudQueueMessage msg)
        {
            string payload = msg.AsString;

            // msg was the one that triggered it.            

            RuntimeBindingInputs ctx = new NewQueueMessageRuntimeBindingInputs(func.Location, msg);

            var instance = BindParameters(ctx, func);

            instance.TriggerReason = new QueueMessageTriggerReason
            {
                MessageId = msg.Id,
                ParentGuid = GetOwnerFromMessage(msg)
            };

            return instance;
        }

        // policy: blobInput is the first [Input] attribute. Functions are triggered by single input.        
        public static FunctionInvokeRequest GetFunctionInvocation(FunctionDefinition func, CloudBlob blobInput)
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
            var instance = BindParameters(ctx, func);

            Guid parentGuid = GetBlobWriterGuid(blobInput);
            instance.TriggerReason = new BlobTriggerReason
            {
                BlobPath = new CloudBlobPath(blobInput),
                ParentGuid = parentGuid
            };
            return instance;
        }

        public void Dispose()
        {
            _listener.Dispose();
        }

        // Bind the entire flow to an instance
        public static FunctionInvokeRequest BindParameters(RuntimeBindingInputs ctx, FunctionDefinition func)
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

        // plug into Trigger Service to queue invocations on triggers. 
        class MyInvoker : ITriggerInvoke
        {
            private readonly Worker _parent;
            public MyInvoker(Worker parent)
            {
                _parent = parent;
            }

            void ITriggerInvoke.OnNewTimer(TimerTrigger trigger, CancellationToken token)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewTimer(func);
            }

            void ITriggerInvoke.OnNewQueueItem(CloudQueueMessage msg, QueueTrigger trigger, CancellationToken token)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewQueueItem(msg, func);
            }

            void ITriggerInvoke.OnNewBlob(CloudBlob blob, BlobTrigger trigger, CancellationToken token)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewBlob(func, blob);
            }
        }
    }
}