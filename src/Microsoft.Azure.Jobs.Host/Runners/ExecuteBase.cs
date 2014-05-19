using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;

namespace Microsoft.Azure.Jobs
{
    // Class to ensure a consistent execution experience w.r.t. logging, etc.
    internal static class ExecutionBase
    {
        public static FunctionInvocationResult Work(
            FunctionInvokeRequest instance, // specific request to execute.
            FunctionExecutionContext context, // provides services for execution. Not request specific

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // The incoming CloudBlobDescriptor is for parameter logging.
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, CloudBlobDescriptor, FunctionExecutionResult> fpInvokeFunc
            )
        {
            IFunctionInstanceLogger instanceLogger = context.FunctionInstanceLogger;
            IFunctionOuputLogDispenser outputLogDispenser = context.OutputLogDispenser;

            DateTimeOffset now = DateTimeOffset.UtcNow;

            FunctionStartedSnapshot startedSnapshot = new FunctionStartedSnapshot
            {
                FunctionInstanceId = instance.Id,
                HostId = context.HostId,
                HostInstanceId = context.HostInstanceId,
                FunctionId = instance.Location.GetId(),
                FunctionFullName = instance.Location.FullName,
                FunctionShortName = instance.Location.GetShortName(),
                Arguments = CreateArguments(instance.Args),
                ParentId = instance.TriggerReason != null && instance.TriggerReason.ParentGuid != Guid.Empty
                    ? (Guid?)instance.TriggerReason.ParentGuid : null,
                Reason = instance.TriggerReason != null ? instance.TriggerReason.ToString() : null,
                StartTime = now,
                StorageConnectionString = instance.Location.AccountConnectionString,
                ServiceBusConnectionString = instance.Location.ServiceBusConnectionString,
                WebJobRunIdentifier = WebJobRunIdentifier.Current
            };

            FunctionCompletedSnapshot completedSnapshot = null;

            try
            {
                completedSnapshot = Work(instance, fpInvokeFunc, instanceLogger, outputLogDispenser, startedSnapshot);
            }
            finally
            {
                if (completedSnapshot == null)
                {
                    completedSnapshot = CreateCompletedSnapshot(startedSnapshot);
                    completedSnapshot.Succeeded = false;
                }

                // User errors returned via results in inner Work()
                completedSnapshot.EndTime = DateTimeOffset.UtcNow;

                instanceLogger.LogFunctionCompleted(completedSnapshot);
            }

            return new FunctionInvocationResult
            {
                Id = completedSnapshot.FunctionInstanceId,
                Succeeded = completedSnapshot.Succeeded,
                ExceptionMessage = completedSnapshot.ExceptionMessage
            };
        }

        // Have confirmed the function exists.  Do real work.
        static FunctionCompletedSnapshot Work(
            FunctionInvokeRequest instance,         // specific request to execute

            // Do the actual invocation. Throw an OperationCancelException is the function is cancelled mid-execution. 
            // The incoming TextWriter is where console output should be redirected too. 
            // The incoming CloudBlobDescriptor is for parameter logging.
            // Returns a FunctionExecutionResult that describes the execution results of the function. 
            Func<TextWriter, CloudBlobDescriptor, FunctionExecutionResult> fpInvokeFunc,

            IFunctionInstanceLogger instanceLogger,
            IFunctionOuputLogDispenser outputLogDispenser,
            FunctionStartedSnapshot startedSnapshot
            )
        {
            FunctionOutputLog functionOutput = outputLogDispenser.CreateLogStream(instance);
            startedSnapshot.OutputBlobUrl = functionOutput.Uri;
            startedSnapshot.ParameterLogBlobUrl = functionOutput.ParameterLogBlob == null ? null : functionOutput.ParameterLogBlob.GetBlockBlob().Uri.AbsoluteUri;

            instanceLogger.LogFunctionStarted(startedSnapshot);

            FunctionCompletedSnapshot completedSnapshot = CreateCompletedSnapshot(startedSnapshot);

            try
            {
                // Invoke the function. Redirect all console output to the given stream.
                // (Function may be invoked in a different process, so we can't just set Console.Out here)
                FunctionExecutionResult result = fpInvokeFunc(functionOutput.Output, functionOutput.ParameterLogBlob);

                // User errors should be caught and returned in result message.
                completedSnapshot.Succeeded = String.IsNullOrEmpty(result.ExceptionMessage);
                completedSnapshot.ExceptionType = result.ExceptionType;
                completedSnapshot.ExceptionMessage = result.ExceptionMessage;
            }
            catch (Exception e)
            {
                if ((e is OperationCanceledException)) // user app exited (probably stack overflow or call to Exit)
                {
                    completedSnapshot.Succeeded = false;
                    completedSnapshot.ExceptionType = e.GetType().FullName;
                    completedSnapshot.ExceptionMessage = e.Message;

                    return completedSnapshot;
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

            return completedSnapshot;
        }

        private static FunctionCompletedSnapshot CreateCompletedSnapshot(FunctionStartedSnapshot startedSnapshot)
        {
            return new FunctionCompletedSnapshot
            {
                FunctionInstanceId = startedSnapshot.FunctionInstanceId,
                HostId = startedSnapshot.HostId,
                HostInstanceId = startedSnapshot.HostInstanceId,
                FunctionId = startedSnapshot.FunctionId,
                FunctionFullName = startedSnapshot.FunctionFullName,
                FunctionShortName = startedSnapshot.FunctionShortName,
                Arguments = startedSnapshot.Arguments,
                ParentId = startedSnapshot.ParentId,
                Reason = startedSnapshot.Reason,
                StartTime = startedSnapshot.StartTime,
                StorageConnectionString = startedSnapshot.StorageConnectionString,
                ServiceBusConnectionString = startedSnapshot.ServiceBusConnectionString,
                OutputBlobUrl = startedSnapshot.OutputBlobUrl,
                ParameterLogBlobUrl = startedSnapshot.ParameterLogBlobUrl,
                WebJobRunIdentifier = startedSnapshot.WebJobRunIdentifier                
            };
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
