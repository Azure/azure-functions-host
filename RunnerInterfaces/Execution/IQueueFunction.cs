using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // Activate a function that was attempted to be queued, but didn't yet have prereqs. 
    public interface IActivateFunction
    {
        void ActivateFunction(Guid instance);
    }

    // Submit a function for execution.
    // This includes queuing the function as well as updating all associated logging. 
    public interface IQueueFunction
    {
        // If instance has prereqs, this may result in a call to IPrereqTable.AddPrereq, 
        // which may result in a callback to ActivateFunction when the prereqs are satisfied. 
        ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance);                
    }
}
