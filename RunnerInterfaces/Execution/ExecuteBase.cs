using System;
using System.IO;
using System.Threading;
using Executor;

namespace RunnerInterfaces
{
    // Various objects needed for execution.
    public class FunctionExecutionContext
    {
        public IFunctionOuputLogDispenser OutputLogDispenser { get; set; }

        // Used to update function as its being executed
        public IFunctionUpdatedLogger Logger { get; set; }
                
        // Mark when a function has finished execution. This will send a message that causes the function's 
        // execution statistics to get aggregated. 
        public ExecutionStatsAggregatorBridge Bridge { get; set; }
    }

    // Class to ensure a consistent execution experience w.r.t. logging, ExecutionInstanceLogEntity, etc. 
    // This is coupled to QueueFunctionBase.
    public class ExecutionBase
    {
        public static void Work(
            FunctionInvokeRequest instance,  // specific request to execute.
            FunctionExecutionContext context, // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc
            )
        {
            var logItem = new ExecutionInstanceLogEntity();

            var logger = context.Logger;
            var bridge = context.Bridge;

            FunctionOutputLog logInfo = context.OutputLogDispenser.CreateLogStream(instance);

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
                catch (Exception e)
                {
                    if ((e is OperationCanceledException) ||  // Execution was aborted. Common case, not a critical error. 
                        (e is AbnormalTerminationException)) // user app exited (probably stack overflow or call to Exit)
                    {                        
                        logItem.ExceptionType = e.GetType().FullName;
                        logItem.ExceptionMessage = e.Message;

                        return;
                    }
                
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