using System;

namespace Microsoft.WindowsAzure.Jobs
{
    // FunctionExecutionContext is the common execution operations that aren't Worker-role specific.
    // Everything else is worker role specific. 
    internal interface IExecutionLogger
    {
        FunctionExecutionContext GetExecutionContext();

        void LogFatalError(string info, Exception e);
                
        // Write health status for the worker role. 
        void WriteHeartbeat(ExecutionRoleHeartbeat stats);

        // Check if a delete is requested and then set a Cancellation token 
        // The communication here could be from detecting a blob; or it could be from WorkerRole-WorkerRole communication.
        bool IsDeleteRequested(Guid id);
    }    
}
