using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Executors;
using Microsoft.Azure.Jobs.Host.Indexers;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    internal class Worker
    {
        private readonly IFunctionTableLookup _functionTable;
        private readonly IExecuteFunction _executor;
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly INotifyNewBlob _notifyNewBlob;
        // Fast-path blob listener. 
        private readonly INotifyNewBlobListener _blobListener;

        // General purpose listener for blobs, queues. 
        private Listener _listener;

        public Worker(IFunctionTableLookup functionTable, IExecuteFunction execute,
            IFunctionInstanceLogger functionInstanceLogger, INotifyNewBlob notifyNewBlob,
            INotifyNewBlobListener blobListener, Credentials credentials)
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
            _executor = execute;
            _functionInstanceLogger = functionInstanceLogger;
            _notifyNewBlob = notifyNewBlob;
            _blobListener = blobListener;

            CreateInputMap(credentials);
        }

        // Called once at startup to initialize orchestration data structures
        // This is just retrieving the data structures created by the Indexer.
        private void CreateInputMap(Credentials credentials)
        {
            FunctionDefinition[] funcs = _functionTable.ReadAll();

            TriggerMap map = new TriggerMap();

            foreach (var func in funcs)
            {
                var ts = CalculateTriggers.GetTrigger(func, credentials);
                if (ts != null)
                {
                    map.AddTriggers(func.Descriptor.Id, ts);
                }
            }

            _listener = new Listener(map, new MyInvoker(this), this);
        }

        private int _triggerCount = 0;

        public void Poll(HostBindingContext context)
        {
            while (!context.CancellationToken.IsCancellationRequested)
            {
                int lastCount = _triggerCount;

                // this is a fast poll (checking a queue), so give it high priority
                PollNotifyNewBlobs(context);
                if (_triggerCount != lastCount)
                {
                    // This is a critical optimization.
                    // If a function writes a blob, immediately execute any functions that would
                    // have been triggered by that blob. Don't wait for a slow blob polling to detect it. 
                    continue;
                }

                _listener.Poll(context);

                if (_triggerCount != lastCount)
                {
                    continue;
                }
                break;
            }
        }

        // Poll blob notifications from the fast path that may be detected ahead of our
        // normal listeners. 
        void PollNotifyNewBlobs(HostBindingContext context)
        {
            if (_blobListener != null)
            {
                _blobListener.ProcessMessages(this.NewBlob, context);
            }
        }

        // Called if the external system thinks we may have a new blob. 
        public void NewBlob(BlobWrittenMessage msg, HostBindingContext context)
        {
            _listener.InvokeTriggersForBlob(msg.AccountName, msg.ContainerName, msg.BlobName, context);
        }

        public void OnNewInvokeableItem(IFunctionInstance instance, HostBindingContext context)
        {
            if (instance != null)
            {
                Interlocked.Increment(ref _triggerCount);
                _executor.Execute(instance, context);
            }
        }

        // Supports explicitly invoking any functions associated with this blob. 
        private void OnNewBlob(FunctionDefinition func, ICloudBlob blob, HostBindingContext context)
        {
            IFunctionInstance instance = CreateFunctionInstance(func, context, blob);
            if (instance != null)
            {
                Interlocked.Increment(ref _triggerCount);
                _executor.Execute(instance, context);
            }
        }

        private static Guid? GetBlobWriterGuid(ICloudBlob blob)
        {
            return BlobCausalityLogger.GetWriter(blob);
        }

        internal static IFunctionInstance CreateFunctionInstance(FunctionDefinition func, HostBindingContext context, ICloudBlob blobInput)
        {
            return new FunctionInstance(Guid.NewGuid(),
                GetBlobWriterGuid(blobInput),
                ExecutionReason.AutomaticTrigger,
                new TriggerBindingSource<ICloudBlob>((ITriggeredFunctionBinding<ICloudBlob>)func.Binding, blobInput),
                func.Descriptor,
                func.Method);
        }

        // plug into Trigger Service to queue invocations on triggers. 
        private class MyInvoker : ITriggerInvoke
        {
            private readonly Worker _parent;
            public MyInvoker(Worker parent)
            {
                _parent = parent;
            }

            void ITriggerInvoke.OnNewBlob(ICloudBlob blob, BlobTrigger trigger, HostBindingContext context)
            {
                FunctionDefinition func = (FunctionDefinition)trigger.Tag;
                _parent.OnNewBlob(func, blob, context);
            }
        }
    }
}
