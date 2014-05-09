using System;
using System.Threading;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Base class for providing a consistent implementation of IQueueFunction
    // this provides consistent treatment of logging facilities around submitting a function (eg, ExecutionInstanceLogEntity)
    // but abstracts away the actual raw queuing mechanism.
    internal abstract class ExecuteFunctionBase : IExecuteFunction
    {
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

            // Execute immediately.
            return Work(instance, cancellationToken);
        }

        // Does the actual queueing mechanism (submit to an azure queue, submit as an azure task)
        protected abstract ExecutionInstanceLogEntity Work(FunctionInvokeRequest instance, CancellationToken cancellationToken);
    }
}
