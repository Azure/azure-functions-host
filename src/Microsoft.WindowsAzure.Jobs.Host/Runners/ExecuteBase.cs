using System;
using System.Diagnostics.Contracts;
using System.IO;
using System.Threading;

namespace Microsoft.WindowsAzure.Jobs
{
    // Class to ensure a consistent execution experience w.r.t. logging, ExecutionInstanceLogEntity, etc. 
    // This is coupled to QueueFunctionBase.
    internal static class ExecutionBase
    {
        static Exception notFoundException = new System.EntryPointNotFoundException("Function not found");

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
            IFunctionInstanceLoggerContext logItemContext;

            if (bridge != null)
            {
                logItemContext = bridge.CreateContext(logItem);
            }
            else
            {
                logItemContext = null;
            }

            logItem.HostInstanceId = context.HostInstanceId;
            logItem.FunctionInstance = instance;
            logItem.StartTime = DateTime.UtcNow;

            try
            {
                // Confirm function exists if provided a function table.  Fake an Exception if not found.
                bool functionExists = true;
                if (context.FunctionTable != null)
                {
                    FunctionDefinition tableLocation = context.FunctionTable.Lookup(instance.Location);
                    functionExists = tableLocation != null;
                }

                if (functionExists)
                {
                    Work(instance, context, fpInvokeFunc, logItem, logItemContext);
                }
                else
                {
                    logItem.ExceptionMessage = notFoundException.Message;
                    logItem.ExceptionType = notFoundException.GetType().FullName;
                }
            }
            finally
            {
                // User errors returned via results in inner Work()
                logItem.EndTime = DateTime.UtcNow;
                logger.Log(logItem);

                if (logItemContext != null)
                {
                    logItemContext.IndexCompletedFunction();
                    logItemContext.Flush();
                }
            }
        }

        // Have confirmed the function exists.  Do real work.
        static void Work(
            FunctionInvokeRequest instance,         // specific request to execute
            FunctionExecutionContext context,       // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, FunctionExecutionResult> fpInvokeFunc,

            ExecutionInstanceLogEntity logItem,   // current request log entity
            IFunctionInstanceLoggerContext logItemContext
            )
        {
            FunctionOutputLog functionOutput = context.OutputLogDispenser.CreateLogStream(instance);
            instance.ParameterLogBlob = functionOutput.ParameterLogBlob;
            logItem.OutputUrl = functionOutput.Uri;

            IFunctionUpdatedLogger logger = context.Logger;
            logger.Log(logItem);

            if (logItemContext != null)
            {
                logItemContext.IndexRunningFunction();
                logItemContext.Flush();
            }

            using (IntervalSeparationTimer timer = CreateHeartbeatTimer(logger, logItem))
            {
                timer.Start(executeFirst: true);

                try
                {
                    // Invoke the function. Redirect all console output to the given stream.
                    // (Function may be invoked in a different process, so we can't just set Console.Out here)
                    FunctionExecutionResult result = fpInvokeFunc(functionOutput.Output);

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
                    functionOutput.Output.WriteLine("Error: {0}", e.Message);
                    functionOutput.Output.WriteLine("stack: {0}", e.StackTrace);
                    throw;
                }
                finally
                {
                    timer.Stop();
                    functionOutput.CloseOutput();
                }
            }
        }

        private static IntervalSeparationTimer CreateHeartbeatTimer(IFunctionUpdatedLogger logger,
            ExecutionInstanceLogEntity logItem)
        {
            TimeSpan normalHeartbeatInterval = TimeSpan.FromSeconds(30);
            TimeSpan heartbeatInvalidationInterval = TimeSpan.FromSeconds(45);
            TimeSpan minimumHeartbeatAttemptInterval = TimeSpan.FromSeconds(10);
            ICanFailCommand command = new UpdateFunctionHeartbeatCommand(logger, logItem, heartbeatInvalidationInterval);
            return LinearSpeedupTimerCommand.CreateTimer(command, normalHeartbeatInterval, minimumHeartbeatAttemptInterval);
        }
    }
}
