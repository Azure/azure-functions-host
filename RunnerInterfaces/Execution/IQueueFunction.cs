using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // Submit a function for execution.
    // This includes queuing the function as well as updating all associated logging. 
    public interface IQueueFunction
    {
        ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance);
    }
}
