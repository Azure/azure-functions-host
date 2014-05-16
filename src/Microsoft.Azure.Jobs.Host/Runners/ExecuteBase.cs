using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;

namespace Microsoft.Azure.Jobs
{
    // Class to ensure a consistent execution experience w.r.t. logging, ExecutionInstanceLogEntity, etc. 
    // This is coupled to QueueFunctionBase.
    internal static class ExecutionBase
    {
        public static ExecutionInstanceLogEntity Work(
            FunctionInvokeRequest instance, // specific request to execute.
            FunctionExecutionContext context, // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // The incoming CloudBlobDescriptor is for parameter logging.
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, CloudBlobDescriptor, FunctionExecutionResult> fpInvokeFunc
            )
        {
            var logItem = new ExecutionInstanceLogEntity();
            logItem.FunctionInstance = instance;

            IFunctionInstanceLogger instanceLogger = context.FunctionInstanceLogger;

            logItem.HostId = context.HostId;
            logItem.HostInstanceId = context.HostInstanceId;
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

                if (instanceLogger != null)
                {
                    instanceLogger.LogFunctionCompleted(CreateCompletedSnapshot(logItem));
                }
            }

            return logItem;
        }

        // Have confirmed the function exists.  Do real work.
        static void Work(
            FunctionInvokeRequest instance,         // specific request to execute
            FunctionExecutionContext context,       // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // The incoming CloudBlobDescriptor is for parameter logging.
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, CloudBlobDescriptor, FunctionExecutionResult> fpInvokeFunc,

            ExecutionInstanceLogEntity logItem,   // current request log entity
            IFunctionInstanceLogger instanceLogger
            )
        {
            FunctionOutputLog functionOutput = context.OutputLogDispenser.CreateLogStream(instance);
            logItem.OutputUrl = functionOutput.Uri;
            logItem.ParameterLogUrl = functionOutput.ParameterLogBlob == null ? null : functionOutput.ParameterLogBlob.GetBlockBlob().Uri.AbsoluteUri;

            if (instanceLogger != null)
            {
                instanceLogger.LogFunctionStarted(CreateStartedSnapshot(logItem));
            }

            try
            {
                // Invoke the function. Redirect all console output to the given stream.
                // (Function may be invoked in a different process, so we can't just set Console.Out here)
                FunctionExecutionResult result = fpInvokeFunc(functionOutput.Output, functionOutput.ParameterLogBlob);

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
                functionOutput.CloseOutput();
            }
        }

        private static FunctionStartedSnapshot CreateStartedSnapshot(ExecutionInstanceLogEntity logEntity)
        {
            return new FunctionStartedSnapshot
            {
                FunctionInstanceId = logEntity.FunctionInstance.Id,
                HostId = logEntity.HostId,
                HostInstanceId = logEntity.HostInstanceId,
                FunctionId = logEntity.FunctionInstance.Location.GetId(),
                FunctionFullName = logEntity.FunctionInstance.Location.FullName,
                FunctionShortName = logEntity.FunctionInstance.Location.GetShortName(),
                Arguments = CreateArguments(logEntity.FunctionInstance.Args),
                ParentId = GetParentId(logEntity),
                Reason = logEntity.FunctionInstance.TriggerReason != null ? logEntity.FunctionInstance.TriggerReason.ToString() : null,
                StartTime = new DateTimeOffset(logEntity.StartTime.Value.ToUniversalTime(), TimeSpan.Zero),
                StorageConnectionString = logEntity.FunctionInstance.Location.AccountConnectionString,
                ServiceBusConnectionString = logEntity.FunctionInstance.Location.ServiceBusConnectionString,
                OutputBlobUrl = logEntity.OutputUrl,
                ParameterLogBlobUrl = logEntity.ParameterLogUrl,
                WebJobRunIdentifier = logEntity.ExecutingJobRunId
            };
        }

        private static FunctionCompletedSnapshot CreateCompletedSnapshot(ExecutionInstanceLogEntity logEntity)
        {
            return new FunctionCompletedSnapshot
            {
                FunctionInstanceId = logEntity.FunctionInstance.Id,
                HostId = logEntity.HostId,
                HostInstanceId = logEntity.HostInstanceId,
                FunctionId = logEntity.FunctionInstance.Location.GetId(),
                FunctionFullName = logEntity.FunctionInstance.Location.FullName,
                FunctionShortName = logEntity.FunctionInstance.Location.GetShortName(),
                Arguments = CreateArguments(logEntity.FunctionInstance.Args),
                ParentId = GetParentId(logEntity),
                Reason = logEntity.FunctionInstance.TriggerReason != null ? logEntity.FunctionInstance.TriggerReason.ToString() : null,
                StartTime = new DateTimeOffset(logEntity.StartTime.Value.ToUniversalTime(), TimeSpan.Zero),
                EndTime = new DateTimeOffset(logEntity.EndTime.Value.ToUniversalTime(), TimeSpan.Zero),
                StorageConnectionString = logEntity.FunctionInstance.Location.AccountConnectionString,
                ServiceBusConnectionString = logEntity.FunctionInstance.Location.ServiceBusConnectionString,
                Succeeded = String.IsNullOrEmpty(logEntity.ExceptionType),
                ExceptionType = logEntity.ExceptionType,
                ExceptionMessage = logEntity.ExceptionMessage,
                OutputBlobUrl = logEntity.OutputUrl,
                ParameterLogBlobUrl = logEntity.ParameterLogUrl,
                WebJobRunIdentifier = logEntity.ExecutingJobRunId
            };
        }

        private static Guid? GetParentId(ExecutionInstanceLogEntity logEntity)
        {
            return logEntity.FunctionInstance.TriggerReason != null
                && logEntity.FunctionInstance.TriggerReason.ParentGuid != Guid.Empty ?
                (Guid?)logEntity.FunctionInstance.TriggerReason.ParentGuid : null;
        }

        private static IDictionary<string, FunctionArgument> CreateArguments(ParameterRuntimeBinding[] runtimeBindings)
        {
            IDictionary<string, FunctionArgument> arguments = new Dictionary<string, FunctionArgument>();

            foreach (ParameterRuntimeBinding runtimeBinding in runtimeBindings)
            {
                BlobParameterRuntimeBinding blobRuntimeBinding = runtimeBinding as BlobParameterRuntimeBinding;
                string value = runtimeBinding.ConvertToInvokeString();
                FunctionArgument argument = new FunctionArgument { Value = value };

                if (blobRuntimeBinding != null)
                {
                    argument.IsBlob = true;
                    argument.IsBlobInput = blobRuntimeBinding.IsInput;
                }

                arguments.Add(runtimeBinding.Name, argument);
            }

            return arguments;
        }
    }
}
