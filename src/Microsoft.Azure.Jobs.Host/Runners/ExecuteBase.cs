using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Runners;
using Microsoft.Azure.Jobs.Host.Triggers;

namespace Microsoft.Azure.Jobs
{
    // Class to ensure a consistent execution experience w.r.t. logging, etc.
    internal static class ExecutionBase
    {
        public static FunctionInvocationResult Work(
            RuntimeBindingProviderContext runtimeContext,
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

            FunctionStartedSnapshot startedSnapshot = new FunctionStartedSnapshot
            {
                FunctionInstanceId = instance.Id,
                HostId = context.HostId,
                HostInstanceId = context.HostInstanceId,
                FunctionId = instance.Location.GetId(),
                FunctionFullName = instance.Location.FullName,
                FunctionShortName = instance.Location.GetShortName(),
                ParentId = instance.TriggerReason != null && instance.TriggerReason.ParentGuid != Guid.Empty
                    ? (Guid?)instance.TriggerReason.ParentGuid : null,
                Reason = instance.TriggerReason != null ? instance.TriggerReason.ToString() : null,
                StartTime = DateTimeOffset.UtcNow,
                StorageConnectionString = instance.Location.StorageConnectionString,
                ServiceBusConnectionString = instance.Location.ServiceBusConnectionString,
                WebJobRunIdentifier = WebJobRunIdentifier.Current
            };

            FunctionCompletedSnapshot completedSnapshot = null;

            try
            {
                FunctionOutputLog functionOutput = outputLogDispenser.CreateLogStream(instance);
                startedSnapshot.OutputBlobUrl = functionOutput.Uri;
                startedSnapshot.ParameterLogBlobUrl = functionOutput.ParameterLogBlob == null ? null : functionOutput.ParameterLogBlob.GetBlockBlob().Uri.AbsoluteUri;

                runtimeContext.ConsoleOutput = functionOutput.Output;
                BindParameters(runtimeContext, instance);
                startedSnapshot.Arguments = CreateArguments(instance.NonTriggerBindings, instance.Parameters, instance.Args);

                instanceLogger.LogFunctionStarted(startedSnapshot);

                completedSnapshot = Work(instance, fpInvokeFunc, instanceLogger, functionOutput, startedSnapshot);
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

        internal static void BindParameters(RuntimeBindingProviderContext context, FunctionInvokeRequest request)
        {
            request.Parameters = BindParameters(context, request.Id, request.TriggerParameterName,
                request.TriggerData, request.NonTriggerBindings);
        }

        internal static IReadOnlyDictionary<string, IValueProvider> BindParameters(RuntimeBindingProviderContext runtimeContext,
            Guid functionInstanceId, string triggerParameterName, ITriggerData triggerData,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            Dictionary<string, IValueProvider> parameters = new Dictionary<string, IValueProvider>();

            if (triggerParameterName != null)
            {
                parameters.Add(triggerParameterName, triggerData.ValueProvider);
            }

            BindingContext context = new BindingContext
            {
                FunctionInstanceId = functionInstanceId,
                NotifyNewBlob = runtimeContext.NotifyNewBlob,
                CancellationToken = runtimeContext.CancellationToken,
                ConsoleOutput = runtimeContext.ConsoleOutput,
                NameResolver = runtimeContext.NameResolver,
                StorageAccount = runtimeContext.StorageAccount,
                ServiceBusConnectionString = runtimeContext.ServiceBusConnectionString,
                BindingData = triggerData != null ? triggerData.BindingData : null,
                BindingProvider = runtimeContext.BindingProvider
            };

            if (nonTriggerBindings != null)
            {
                foreach (KeyValuePair<string, IBinding> item in nonTriggerBindings)
                {
                    parameters.Add(item.Key, item.Value.Bind(context));
                }
            }

            return parameters;
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
            FunctionOutputLog functionOutput,
            FunctionStartedSnapshot startedSnapshot
            )
        {
            FunctionCompletedSnapshot completedSnapshot = CreateCompletedSnapshot(startedSnapshot);

            try
            {
                // Invoke the function, using the output and parameter log blobs just created.
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

        private static IDictionary<string, FunctionArgument> CreateArguments(
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            IReadOnlyDictionary<string, IValueProvider> parameters,
            ParameterRuntimeBinding[] runtimeBindings)
        {
            IDictionary<string, FunctionArgument> arguments = new Dictionary<string, FunctionArgument>();

            if (parameters != null)
            {
                foreach (KeyValuePair<string, IValueProvider> parameter in parameters)
                {
                    IValueProvider valueProvider = parameter.Value;

                    FunctionArgument argument = new FunctionArgument { Value = valueProvider.ToInvokeString() };

                    string name = parameter.Key;

                    if (nonTriggerBindings.ContainsKey(name))
                    {
                        BlobBinding binding = nonTriggerBindings[name] as BlobBinding;

                        if (binding != null)
                        {
                            argument.IsBlob = true;
                            argument.IsBlobInput = binding.IsInput;
                        }
                    }

                    arguments.Add(parameter.Key, argument);
                }
            }

            foreach (ParameterRuntimeBinding runtimeBinding in runtimeBindings)
            {
                if (arguments.ContainsKey(runtimeBinding.Name))
                {
                    continue;
                }

                string value = runtimeBinding.ConvertToInvokeString();
                FunctionArgument argument = new FunctionArgument { Value = value };

                arguments.Add(runtimeBinding.Name, argument);
            }

            return arguments;
        }
    }
}
