using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Runners
{
    // In-memory executor. 
    class WebSitesExecuteFunction : IExecuteFunction
    {
        private readonly FunctionExecutionContext _sharedContext;

        public WebSitesExecuteFunction(FunctionExecutionContext sharedContext)
        {
            _sharedContext = sharedContext;
        }

        public FunctionInvocationResult Execute(FunctionInvokeRequest request, RuntimeBindingProviderContext context)
        {
            if (request.TriggerReason == null)
            {
                // Having a trigger reason is important for diagnostics. 
                // So make sure it's not accidentally null. 
                throw new InvalidOperationException("Function instance must have a trigger reason set.");
            }

            request.TriggerReason.ChildGuid = request.Id;

            FunctionStartedMessage startedMessage = CreateStartedMessageWithoutArguments(request,
                context.StorageAccount, context.ServiceBusConnectionString);
            FunctionCompletedMessage completedMessage = null;

            try
            {
                ExecuteWithLogMessage(request, context, startedMessage);
                completedMessage = CreateCompletedMessage(startedMessage);
                completedMessage.Succeeded = true;
            }
            catch (Exception e)
            {
                if (completedMessage == null)
                {
                    completedMessage = CreateCompletedMessage(startedMessage);
                }

                completedMessage.Succeeded = false;
                completedMessage.ExceptionType = e.GetType().FullName;
                completedMessage.ExceptionMessage = e.Message;
            }
            finally
            {
                completedMessage.EndTime = DateTimeOffset.UtcNow;
                _sharedContext.FunctionInstanceLogger.LogFunctionCompleted(completedMessage);
            }

            return new FunctionInvocationResult
            {
                Id = completedMessage.FunctionInstanceId,
                Succeeded = completedMessage.Succeeded,
                ExceptionMessage = completedMessage.ExceptionMessage
            };
        }

        private void ExecuteWithLogMessage(FunctionInvokeRequest request, RuntimeBindingProviderContext context, FunctionStartedMessage message)
        {
            // Create the console output writer
            FunctionOutputLog functionOutput = _sharedContext.OutputLogDispenser.CreateLogStream(request);
            TextWriter consoleOutput = functionOutput.Output;
            context.ConsoleOutput = consoleOutput;

            // Must bind before logging (bound invoke string is included in log message).
            IReadOnlyDictionary<string, IValueProvider> parameters = request.ParametersProvider.Bind();

            using (ValueProviderDisposable.Create(parameters))
            {
                IReadOnlyDictionary<string, IBinding> nonTriggerBindings = request.ParametersProvider.NonTriggerBindings;
                LogFunctionStarted(message, functionOutput, parameters, nonTriggerBindings);

                try
                {
                    ExecuteWithOutputLogs(request, parameters, consoleOutput, functionOutput.ParameterLogBlob);
                }
                catch (Exception exception)
                {
                    consoleOutput.WriteLine("--------");
                    consoleOutput.WriteLine("Exception while executing:");
                    WriteExceptionChain(exception, consoleOutput);
                    throw;
                }
                finally
                {
                    functionOutput.CloseOutput();
                }
            }
        }

        private void LogFunctionStarted(FunctionStartedMessage message, FunctionOutputLog functionOutput,
            IReadOnlyDictionary<string, IValueProvider> parameters,
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings)
        {
            // Finish populating the function started snapshot.
            message.OutputBlobUrl = functionOutput.Uri;
            CloudBlobDescriptor parameterLogger = functionOutput.ParameterLogBlob;
            message.ParameterLogBlobUrl = parameterLogger == null ? null : parameterLogger.GetBlockBlob().Uri.AbsoluteUri;
            message.Arguments = CreateArguments(nonTriggerBindings, parameters);

            // Log that the function started.
            _sharedContext.FunctionInstanceLogger.LogFunctionStarted(message);
        }

        private void ExecuteWithOutputLogs(FunctionInvokeRequest request,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput,
            CloudBlobDescriptor parameterLogger)
        {
            MethodInfo method = request.Method;
            ParameterInfo[] parameterInfos = method.GetParameters();
            SelfWatch selfWatch = CreateSelfWatch(parameterLogger, parameterInfos, parameters, consoleOutput);

            try
            {
                ExecuteWithSelfWatch(method, parameterInfos, parameters, consoleOutput);
            }
            finally
            {
                // Stop the watches after calling IValueBinder.SetValue (it may do things that should show up in the
                // watches).
                // Also, IValueBinder.SetValue could also take a long time (flushing large caches), and so it's useful
                // to have watches still running.                
                if (selfWatch != null)
                {
                    selfWatch.Stop();
                }
            }
        }

        private static SelfWatch CreateSelfWatch(CloudBlobDescriptor parameterLogger, ParameterInfo[] parameterInfos,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput)
        {
            if (parameterLogger == null)
            {
                return null;
            }

            ISelfWatch[] watches = new ISelfWatch[parameterInfos.Length];

            for (int index = 0; index < parameterInfos.Length; index++)
            {
                ParameterInfo parameterInfo = parameterInfos[index];
                string name = parameterInfo.Name;
                IValueProvider valueProvider = parameters[name];

                IWatchable watchable = valueProvider as IWatchable;

                if (watchable != null)
                {
                    watches[index] = watchable.Watcher;
                }
            }

            CloudBlockBlob parameterBlob = parameterLogger.GetBlockBlob();
            return new SelfWatch(watches, parameterBlob, consoleOutput);
        }

        internal static void ExecuteWithSelfWatch(MethodInfo method, ParameterInfo[] parameterInfos,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput)
        {
            AggregateException aggregateBindingException;
            object[] reflectionParameters = PrepareParameters(parameterInfos, parameters, out aggregateBindingException);

            if (aggregateBindingException != null)
            {
                // This is done inside a self watch context so that each binding error is publish next to the binding in
                // the self watch log.
                throw aggregateBindingException;
            }

            if (IsAsyncMethod(method))
            {
                InformNoAsyncSupport(consoleOutput);
            }

            try
            {
                object returnValue = method.Invoke(null, reflectionParameters);

                HandleFunctionReturnValue(method, returnValue, consoleOutput);
            }
            catch (TargetInvocationException exception)
            {
                // $$$ Beware, this loses the stack trace from the user's invocation
                // Print stacktrace to console now while we have it.
                consoleOutput.WriteLine(exception.InnerException.StackTrace);

                throw exception.InnerException;
            }
            finally
            {
                // Process any out parameters, do any cleanup
                // For update, do any cleanup work. 

                // Ensure IValueBinder.SetValue is called in BindOrder. This ordering is particularly important for
                // ensuring queue outputs occur last. That way, all other function side-effects are guaranteed to have
                // occurred by the time messages are enqueued.
                string[] parameterNamesInBindOrder = SortParameterNamesInStepOrder(parameters);

                foreach (string name in parameterNamesInBindOrder)
                {
                    IValueProvider provider = parameters[name];
                    IValueBinder binder = provider as IValueBinder;

                    if (binder != null)
                    {
                        object argument = reflectionParameters[GetParameterIndex(parameterInfos, name)];

                        try
                        {
                            // This could do complex things that may fail. Catch the exception.
                            binder.SetValue(argument);
                        }
                        catch (Exception e)
                        {
                            string msg = string.Format("Error while handling parameter {0} '{1}' after function returned:", name, argument);
                            throw new InvalidOperationException(msg, e);
                        }
                    }
                }
            }
        }

        private static object[] PrepareParameters(ParameterInfo[] parameterInfos, IReadOnlyDictionary<string, IValueProvider> parameters,
            out AggregateException aggregateBindingException)
        {
            object[] reflectionParameters = new object[parameterInfos.Length];
            List<Exception> bindingExceptions = new List<Exception>();

            for (int index = 0; index < parameterInfos.Length; index++)
            {
                string name = parameterInfos[index].Name;
                IValueProvider provider = parameters[name];

                BindingExceptionValueProvider exceptionProvider = provider as BindingExceptionValueProvider;

                if (exceptionProvider != null)
                {
                    bindingExceptions.Add(exceptionProvider.Exception);
                }

                reflectionParameters[index] = parameters[name].GetValue();
            }

            if (bindingExceptions.Count == 0)
            {
                aggregateBindingException = null;
            }
            else
            {
                aggregateBindingException = new AggregateException(bindingExceptions);
            }

            return reflectionParameters;
        }

        private FunctionStartedMessage CreateStartedMessageWithoutArguments(FunctionInvokeRequest request,
            CloudStorageAccount storageAccount, string serviceBusConnectionString)
        {
            TriggerReason triggerReason = request.TriggerReason;

            return new FunctionStartedMessage
            {
                FunctionInstanceId = request.Id,
                HostId = _sharedContext.HostId,
                HostInstanceId = _sharedContext.HostInstanceId,
                FunctionId = request.Method.GetFullName(),
                FunctionFullName = request.Method.GetFullName(),
                FunctionShortName = request.Method.GetShortName(),
                ParentId = triggerReason != null && triggerReason.ParentGuid != Guid.Empty
                    ? (Guid?)triggerReason.ParentGuid : null,
                Reason = triggerReason != null ? triggerReason.ToString() : null,
                StartTime = DateTimeOffset.UtcNow,
                StorageConnectionString = storageAccount != null ? storageAccount.ToString(exportSecrets: true) : null,
                ServiceBusConnectionString = serviceBusConnectionString,
                WebJobRunIdentifier = WebJobRunIdentifier.Current
            };
        }

        private static FunctionCompletedMessage CreateCompletedMessage(FunctionStartedMessage startedMessage)
        {
            return new FunctionCompletedMessage
            {
                FunctionInstanceId = startedMessage.FunctionInstanceId,
                HostId = startedMessage.HostId,
                HostInstanceId = startedMessage.HostInstanceId,
                FunctionId = startedMessage.FunctionId,
                FunctionFullName = startedMessage.FunctionFullName,
                FunctionShortName = startedMessage.FunctionShortName,
                Arguments = startedMessage.Arguments,
                ParentId = startedMessage.ParentId,
                Reason = startedMessage.Reason,
                StartTime = startedMessage.StartTime,
                StorageConnectionString = startedMessage.StorageConnectionString,
                ServiceBusConnectionString = startedMessage.ServiceBusConnectionString,
                OutputBlobUrl = startedMessage.OutputBlobUrl,
                ParameterLogBlobUrl = startedMessage.ParameterLogBlobUrl,
                WebJobRunIdentifier = startedMessage.WebJobRunIdentifier
            };
        }

        private static IDictionary<string, FunctionArgument> CreateArguments(
            IReadOnlyDictionary<string, IBinding> nonTriggerBindings,
            IReadOnlyDictionary<string, IValueProvider> parameters)
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

            return arguments;
        }

        // Write an exception and inner exceptions
        public static void WriteExceptionChain(Exception e, TextWriter output)
        {
            Exception e2 = e;
            while (e2 != null)
            {
                output.WriteLine("{0}, {1}", e2.GetType().FullName, e2.Message);

                // Write bonus information for extra diagnostics
                var se = e2 as StorageException;
                if (se != null)
                {
                    var nvc = se.RequestInformation.ExtendedErrorInformation.AdditionalDetails;

                    foreach (var key in nvc.Keys)
                    {
                        output.WriteLine("  >{0}: {1}", key, nvc[key]);
                    }
                }

                output.WriteLine(e2.StackTrace);
                output.WriteLine();
                e2 = e2.InnerException;
            }
        }

        private static int GetParameterIndex(ParameterInfo[] parameters, string name)
        {
            for (int index = 0; index < parameters.Length; index++)
            {
                if (parameters[index].Name == name)
                {
                    return index;
                }
            }

            throw new InvalidOperationException("Cannot find parameter + " + name + ".");
        }

        private static string[] SortParameterNamesInStepOrder(IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            string[] parameterNames = new string[parameters.Count];
            int index = 0;

            foreach (string parameterName in parameters.Keys)
            {
                parameterNames[index] = parameterName;
                index++;
            }

            IValueProvider[] parameterValues = new IValueProvider[parameters.Count];
            index = 0;

            foreach (IValueProvider parameterValue in parameters.Values)
            {
                parameterValues[index] = parameterValue;
                index++;
            }

            Array.Sort(parameterValues, parameterNames, ValueBinderStepOrderComparer.Instance);
            return parameterNames;
        }

        /// <summary>
        /// Handles the function return value and logs it, if necessary
        /// </summary>
        private static void HandleFunctionReturnValue(MethodInfo m, object returnValue, TextWriter consoleOutput)
        {
            Type returnType = m.ReturnType;

            if (returnType == typeof(void))
            {
                // No need to do anything
                return;
            }
            else if (IsAsyncMethod(m))
            {
                Task t = returnValue as Task;
                t.Wait();

                if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
                {
                    PropertyInfo resultProperty = returnType.GetProperty("Result");
                    object result = resultProperty.GetValue(returnValue);

                    LogReturnValue(consoleOutput, result);
                }
            }
            else
            {
                LogReturnValue(consoleOutput, returnValue);
            }
        }

        private static bool IsAsyncMethod(MethodInfo m)
        {
            Type returnType = m.ReturnType;

            return typeof(Task).IsAssignableFrom(returnType);
        }

        private static void InformNoAsyncSupport(TextWriter consoleOutput)
        {
            consoleOutput.WriteLine("Warning: This asynchronous method will be run synchronously.");
        }

        private static void LogReturnValue(TextWriter consoleOutput, object value)
        {
            consoleOutput.WriteLine("Return value: {0}", value != null ? value.ToString() : "<null>");
        }

        private class ValueBinderStepOrderComparer : IComparer<IValueProvider>
        {
            private static readonly ValueBinderStepOrderComparer _instance = new ValueBinderStepOrderComparer();

            private ValueBinderStepOrderComparer()
            {
            }

            public static ValueBinderStepOrderComparer Instance { get { return _instance; } }

            public int Compare(IValueProvider x, IValueProvider y)
            {
                int xOrder = GetStepOrder(x);
                int yOrder = GetStepOrder(y);

                return Comparer<int>.Default.Compare(xOrder, yOrder);
            }

            private static int GetStepOrder(IValueProvider provider)
            {
                IOrderedValueBinder orderedBinder = provider as IOrderedValueBinder;

                if (orderedBinder == null)
                {
                    return BindStepOrders.Default;
                }

                return orderedBinder.StepOrder;
            }
        }
    }
}
