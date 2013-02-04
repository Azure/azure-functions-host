using System;
using System.IO;
using System.Threading;
using Executor;

namespace RunnerInterfaces
{
    public class AzureExecutionEndpointNames
    {

    }
    
    // Various objects needed for execution.
    public class FunctionExecutionContext
    {
        // !!! Only needed to get the FunctionOutputLog. Could replace with a dispenser object, which also encapsulates the name. 
        public IAccountInfo Account { get; set; }
        public Action<TextWriter> FPAddHeaderInfo { get; set; } // function for writing header info after we create  a new log stream

        // Used to update function as its being executed
        public IFunctionUpdatedLogger Logger { get; set; }

        // Mark when a function has finished execution. This will send a message that causes the function's 
        // execution statistics to get aggregated. 
        public ExecutionStatsAggregatorBridge Bridge { get; set; }

        public virtual FunctionOutputLog CreateLogInfo(FunctionInvokeRequest instance)
        {
            string containerName = "daas" + "-invoke-log"; // !!! Share with EndpointNames?
            FunctionOutputLog logInfo = FunctionOutputLog.GetLogStream(
                instance,
                this.Account.AccountConnectionString,
                containerName);

            var x = FPAddHeaderInfo;
            if (x != null)
            {
                FPAddHeaderInfo(logInfo.Output);
            }

            return logInfo;
        }
    }

    // Class to ensure a consistent execution experience w.r.t. logging, ExecutionInstanceLogEntity, etc. 
    // This is coupled to QueueFunctionBase.
    public class ExecutionBase
    {
        public static void Work(
            FunctionInvokeRequest instance,  // specific request to execute.
            FunctionExecutionContext context, // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc
            )
        {
            var logItem = new ExecutionInstanceLogEntity();

            var logger = context.Logger;
            var bridge = context.Bridge;

            FunctionOutputLog logInfo = context.CreateLogInfo(instance);

            instance.ParameterLogBlob = logInfo.ParameterLogBlob;

            logItem.FunctionInstance = instance;
            logItem.OutputUrl = logInfo.Uri;
            logItem.StartTime = DateTime.UtcNow;
            logger.Log(logItem);

            try
            {
                try
                {
                    // Invoke the function. Redirect all console output to the given stream. 
                    // (Function may be invoked in a different process, so we can't just set Console.Out here)
                    FunctionExecutionResult result = fpInvokeFunc(logInfo.Output);

                    // User errors should be caught and returned in result message.
                    logItem.ExceptionType = result.ExceptionType;
                    logItem.ExceptionMessage = result.ExceptionMessage;
                }
                catch (OperationCanceledException e)
                {
                    // Execution was aborted. Common case, not a critical error. 
                    logItem.ExceptionType = e.GetType().FullName;
                    logItem.ExceptionMessage = e.Message;
                }
                catch (Exception e)
                {
                    // Non-user error. Something really bad happened! This shouldn't be happening. 
                    // Suggests something critically wrong with the execution infrastructure that wasn't properly
                    // handled elsewhere. 
                    logInfo.Output.WriteLine("Error: {0}", e.Message);
                    logInfo.Output.WriteLine("stack: {0}", e.StackTrace);
                    throw; 
                }
            }
            finally
            {
                logInfo.CloseOutput();

                // User errors returned via results.
                logItem.EndTime = DateTime.UtcNow;
                logger.Log(logItem);

                // Invoke ExecutionStatsAggregatorBridge to queue a message back for the orchestrator. 
                bridge.EnqueueCompletedFunction(logItem);
            }
        }
    }
}