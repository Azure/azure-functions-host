using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // Base class for providing a consistent implementation of IQueueFunction
    // this provides consistent treatment of logging facilities around submitting a function (eg, ExecutionInstanceLogEntity)
    // but abstracts away the actual raw queuing mechanism.
    public abstract class QueueFunctionBase : IQueueFunction
    {
        protected readonly IFunctionUpdatedLogger _logger;
        protected readonly IAccountInfo _account;
        protected readonly ICausalityLogger _causalityLogger;

        // account - this is the internal storage account for using the service. 
        // logger - used for updating the status of the function that gets queued. This must be serializable with JSon since
        //          it will get passed to the host process in an azure task.
        protected QueueFunctionBase(IAccountInfo account, IFunctionUpdatedLogger logger, ICausalityLogger causalityLogger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException("logger");
            }
            if (account == null)
            {
                throw new ArgumentNullException("account");
            }
            if (causalityLogger == null)
            {
                throw new ArgumentNullException("causalityLogger");
            }

            _account = account;
            _logger = logger;
            _causalityLogger = causalityLogger;
        }

        public ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance)
        {
            instance.Id = Guid.NewGuid(); // used for logging. 
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
            logItem.QueueTime = DateTime.UtcNow; // don't set starttime until a role actually executes it.

            _logger.Log(logItem);

            Work(logItem);

            // Now that it's queued, execution node may immediately pick up the queue item and start running it, 
            // and logging against it.

            return logItem;
        }

        // Does the actual queueing mechanism (submit to an azure queue, submit as an azure task)
        protected abstract void Work(ExecutionInstanceLogEntity logItem);
    }
}
