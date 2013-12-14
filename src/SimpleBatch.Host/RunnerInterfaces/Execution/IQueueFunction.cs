using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace Microsoft.WindowsAzure.Jobs
{
    // Activate a function that was attempted to be queued, but didn't yet have prereqs. 
    internal interface IActivateFunction
    {
        void ActivateFunction(Guid instance);
    }

    // Submit a function for execution.
    // This includes queuing the function as well as updating all associated logging. 
    internal interface IQueueFunction
    {
        // If instance has prereqs, this may result in a call to IPrereqTable.AddPrereq, 
        // which may result in a callback to ActivateFunction when the prereqs are satisfied. 
        ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance);                
    }
}
