using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Executor;

namespace RunnerInterfaces
{
    // Submit a function for execution.
    public interface IQueueFunction
    {
        ExecutionInstanceLogEntity Queue(FunctionInvokeRequest instance);
    }
}
