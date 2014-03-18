using System;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Base class for providing a consistent implementation of IQueueFunction
    // this provides consistent treatment of logging facilities around submitting a function (eg, ExecutionInstanceLogEntity)
    // but abstracts away the actual raw queuing mechanism.
    internal abstract class ExecuteFunctionBase : IExecuteFunction
    {
        protected readonly IFunctionUpdatedLogger _logger;
        protected readonly IFunctionInstanceLookup _lookup;
        protected readonly IAccountInfo _account;
        protected readonly ICausalityLogger _causalityLogger;

        // account - this is the internal storage account for using the service. 
        // logger - used for updating the status of the function that gets executed. This must be serializable with JSon since
        //          it will get passed to the host process in an azure task.
        protected ExecuteFunctionBase(ExecuteFunctionInterfaces interfaces)
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
        }

        public ExecutionInstanceLogEntity Execute(FunctionInvokeRequest instance, CancellationToken cancellationToken)
        {
            if (instance.Id == Guid.Empty)
            {
                instance.Id = Guid.NewGuid(); // used for logging. 
            }

            instance.SchemaNumber = FunctionInvokeRequest.CurrentSchema;

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

            // Execute immediately.
            Work(logItem, cancellationToken);

            // Lookup again. In the bowls of execution, we may have made changes 
            // against the log item in the table instead of our reference here. 
            logItem = _lookup.Lookup(instance.Id);

            return logItem;
        }

        // Does the actual queueing mechanism (submit to an azure queue, submit as an azure task)
        protected abstract void Work(ExecutionInstanceLogEntity logItem, CancellationToken cancellationToken);
    }
}
