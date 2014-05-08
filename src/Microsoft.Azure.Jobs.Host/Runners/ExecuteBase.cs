using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Azure.Jobs
{
    // Class to ensure a consistent execution experience w.r.t. logging, ExecutionInstanceLogEntity, etc. 
    // This is coupled to QueueFunctionBase.
    internal static class ExecutionBase
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
            IFunctionInstanceLogger instanceLogger = context.FunctionInstanceLogger;

            logItem.HostInstanceId = context.HostInstanceId;
            logItem.FunctionInstance = instance;
            DateTime now = DateTime.UtcNow;
            logItem.QueueTime = now;
            logItem.StartTime = now;
            logItem.ExecutingJobRunId = WebJobRunIdentifier.Current;

            try
            {
                Work(instance, context, fpInvokeFunc, logItem, instanceLogger);
            }
            finally
            {
                // User errors returned via results in inner Work()
                logItem.EndTime = DateTime.UtcNow;
                logger.Log(logItem);

                if (instanceLogger != null)
                {
                    instanceLogger.LogFunctionCompleted(logItem);
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
            IFunctionInstanceLogger instanceLogger
            )
        {
            FunctionOutputLog functionOutput = context.OutputLogDispenser.CreateLogStream(instance);
            instance.ParameterLogBlob = functionOutput.ParameterLogBlob;
            logItem.OutputUrl = functionOutput.Uri;

            IFunctionUpdatedLogger logger = context.Logger;
            logger.Log(logItem);

            if (instanceLogger != null)
            {
                instanceLogger.LogFunctionStarted(logItem);
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
                    if ((e is OperationCanceledException)) // user app exited (probably stack overflow or call to Exit)
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

        // Marks a function as failed when it cannot be executed. (For example, the function has been deleted.)
        public static void LogFunctionFailed(IFunctionInstanceLookup functionLookup,
            IFunctionUpdatedLogger functionUpdated,
            Guid functionInstanceId,
            string exceptionType,
            string exceptionMessage)
        {
            Debug.Assert(functionLookup != null);
            Debug.Assert(functionUpdated != null);

            ExecutionInstanceLogEntity log = functionLookup.Lookup(functionInstanceId);

            if (log != null)
            {
                log.StartTime = DateTime.UtcNow;
                log.EndTime = DateTime.UtcNow;
                log.ExceptionType = exceptionType;
                log.ExceptionMessage = exceptionMessage;
                functionUpdated.Log(log);
            }
        }
    }
}
