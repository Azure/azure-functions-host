using System;
using System.Threading;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.WindowsAzure.Storage;

namespace Microsoft.Azure.Jobs
{
    // Base class for providing a consistent implementation of IQueueFunction
    // this provides consistent treatment of logging facilities around submitting a function (eg, ExecutionInstanceLogEntity)
    // but abstracts away the actual raw queuing mechanism.
    internal abstract class ExecuteFunctionBase : IExecuteFunction
    {
        public FunctionInvocationResult Execute(FunctionInvokeRequest instance, RuntimeBindingProviderContext context)
        {
            if (instance.TriggerReason == null)
            {
                // Having a trigger reason is important for diagnostics. 
                // So make sure it's not accidentally null. 
                throw new InvalidOperationException("Function instance must have a trigger reason set.");
            }
            instance.TriggerReason.ChildGuid = instance.Id;

            // Execute immediately.
            return Work(instance, context);
        }

        // Does the actual queueing mechanism (submit to an azure queue, submit as an azure task)
        protected abstract FunctionInvocationResult Work(FunctionInvokeRequest instance, RuntimeBindingProviderContext context);
    }
}
