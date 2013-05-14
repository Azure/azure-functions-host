using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // Bunder of interfaces needed for execution. Grouped together for convenience. 
    public class QueueInterfaces
    {
        public IAccountInfo AccountInfo;
        public IFunctionInstanceLookup Lookup;
        public IFunctionUpdatedLogger Logger;
        public ICausalityLogger CausalityLogger;
        public IPrereqManager PreqreqManager;

        public void VerifyNotNull()
        {
            if (Logger == null)
            {
                throw new ArgumentNullException("Logger");
            }
            if (Lookup == null)
            {
                throw new ArgumentNullException("Lookup");
            }
            if (AccountInfo == null)
            {
                throw new ArgumentNullException("AccountInfo");
            }
            if (CausalityLogger == null)
            {
                throw new ArgumentNullException("CausalityLogger");
            }
            if (PreqreqManager == null)
            {
                throw new ArgumentNullException("PreqreqManager");
            }
        }
    }

    // Base class for providing a consistent implementation of IQueueFunction
    // this provides consistent treatment of logging facilities around submitting a function (eg, ExecutionInstanceLogEntity)
    // but abstracts away the actual raw queuing mechanism.
    public abstract class QueueFunctionBase : IQueueFunction, IActivateFunction
    {
        protected readonly IFunctionUpdatedLogger _logger;
        protected readonly IFunctionInstanceLookup _lookup;
        protected readonly IAccountInfo _account;
        protected readonly ICausalityLogger _causalityLogger;

        private readonly IPrereqManager _preqreqManager;

        // account - this is the internal storage account for using the service. 
        // logger - used for updating the status of the function that gets queued. This must be serializable with JSon since
        //          it will get passed to the host process in an azure task.
        protected QueueFunctionBase(QueueInterfaces interfaces)
        {
            if (interfaces == null)
            {
                throw new ArgumentNullException("interfaces");
            }
            interfaces.VerifyNotNull();

            _lookup = interfaces.Lookup;
            _account = interfaces.AccountInfo;
            _logger = interfaces.Logger;
            _causalityLogger = interfaces.CausalityLogger;
            _preqreqManager = interfaces.PreqreqManager;
        }

        public ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance)
        {
            instance.Id = Guid.NewGuid(); // used for logging. 
            instance.SchemaNumber = FunctionInvokeRequest.CurrentSchema;
            instance.ServiceUrl = _account.WebDashboardUri;

            if (instance.TriggerReason == null)
            {
                // Having a trigger reason is important for diagnostics. 
                // So make sure it's not accidentally null. 
                throw new InvalidOperationException("Function instance must have a trigger reason set.");
            }
            instance.TriggerReason.ChildGuid = instance.Id;
            _causalityLogger.LogTriggerReason(instance.TriggerReason);
            
            // Log that the function is now queued.
            // Do this before queueing to avoid racing with execution 
            var logItem = new ExecutionInstanceLogEntity();
            logItem.FunctionInstance = instance;
            
            if (instance.Prereqs != null && instance.Prereqs.Length > 0)
            {
                // Has prereqs. don't queue yet. Instead, setup in the pre-req table.                
                _logger.Log(logItem);

                _preqreqManager.AddPrereq(instance.Id, instance.Prereqs, this);                
            }
            else
            {
                // No preqs. Can queue for immediate execution. 
                ActivateFunction(logItem);
            }

            // Now that it's queued, execution node may immediately pick up the queue item and start running it, 
            // and logging against it.

            return logItem;
        }

        public void ActivateFunction(Guid func)
        {
            var logItem = _lookup.LookupOrThrow(func);

            ActivateFunction(logItem);
        }

        public void ActivateFunction(ExecutionInstanceLogEntity logItem)
        {
            logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.
            _logger.Log(logItem);

            Work(logItem);
        }

        // Does the actual queueing mechanism (submit to an azure queue, submit as an azure task)
        protected abstract void Work(ExecutionInstanceLogEntity logItem);
    }
}
