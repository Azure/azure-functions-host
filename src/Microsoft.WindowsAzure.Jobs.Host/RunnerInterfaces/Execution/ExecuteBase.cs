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

            using (RunningFunctionHeartbeat heartbeat = new RunningFunctionHeartbeat(logger, logItem))
            {
                // Set an initial heartbeat without relying on another thread to start.
                heartbeat.Signal();

                Thread heartbeatThread = new Thread(FunctionRunningThreadCallback);
                heartbeatThread.Start(heartbeat);

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
                    heartbeat.Cancel();
                    heartbeatThread.Join();
                    functionOutput.CloseOutput();
                }
            }
        }

        static private void FunctionRunningThreadCallback(object state)
        {
            IHeartbeatThread thread = (IHeartbeatThread)state;
            thread.Run();
        }

        private interface IHeartbeatThread
        {
            void Run();
        }

        private sealed class RunningFunctionHeartbeat : IDisposable, IHeartbeatThread
        {
            private const int _heartbeatFrequencyInSeconds = 30;
            private const int _heartbeatInvalidationInSeconds = 45;
            private const int _millisecondsInSecond = 1000;
            private const int _heartbeatFrequencyInMilliseconds = _heartbeatFrequencyInSeconds * _millisecondsInSecond;

            private readonly EventWaitHandle _event = new ManualResetEvent(initialState: false);
            private readonly IFunctionUpdatedLogger _logger;
            private readonly ExecutionInstanceLogEntity _logItem;

            private bool _disposed;

            public RunningFunctionHeartbeat(IFunctionUpdatedLogger logger, ExecutionInstanceLogEntity logItem)
            {
                if (logger == null)
                {
                    throw new ArgumentNullException("logger");
                }

                if (logItem == null)
                {
                    throw new ArgumentNullException("logItem");
                }

                _logger = logger;
                _logItem = logItem;
            }

            public void Cancel()
            {
                ThrowIfDisposed();
                bool succeeded = _event.Set();
                // EventWaitHandle.Set can never return false (see implementation).
                Contract.Assert(succeeded);
            }

            public void Run()
            {
                // Keep signaling until cancelled
                while (!WaitForCancellation())
                {
                    Signal();
                }
            }

            public void Signal()
            {
                Signal(DateTime.UtcNow.AddSeconds(_heartbeatInvalidationInSeconds));
            }

            private void Signal(DateTime invalidAfterUtc)
            {
                ThrowIfDisposed();
                _logItem.HeartbeatExpires = invalidAfterUtc;
                _logger.Log(_logItem);
            }

            private bool WaitForCancellation()
            {
                return _event.WaitOne(_heartbeatFrequencyInMilliseconds);
            }

            void IDisposable.Dispose()
            {
                if (!_disposed)
                {
                    _event.Dispose();
                    _disposed = true;
                }
            }

            private void ThrowIfDisposed()
            {
                if (_disposed)
                {
                    throw new ObjectDisposedException(null);
                }
            }
        }
    }
}
