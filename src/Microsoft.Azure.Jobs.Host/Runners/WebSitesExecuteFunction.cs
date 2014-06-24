using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Blobs.Bindings;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Triggers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using System.Runtime.ExceptionServices;
using Microsoft.Azure.Jobs.Host.Executors;

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

        public FunctionInvocationResult Execute(IFunctionInstance instance, RuntimeBindingProviderContext context)
        {
            FunctionStartedMessage startedMessage = CreateStartedMessageWithoutArguments(instance,
                context.StorageAccount, context.ServiceBusConnectionString);
            IDictionary<string, ParameterLog> parameterLogCollector = new Dictionary<string, ParameterLog>();
            FunctionCompletedMessage completedMessage = null;

            ExceptionDispatchInfo exceptionInfo = null;

            try
            {
                ExecuteWithLogMessage(instance, context, startedMessage, parameterLogCollector);
                completedMessage = CreateCompletedMessage(startedMessage);
            }
            catch (Exception e)
            {
                if (completedMessage == null)
                {
                    completedMessage = CreateCompletedMessage(startedMessage);
                }

                completedMessage.Failure = new FunctionFailure
                {
                    ExceptionType = e.GetType().FullName,
                    ExceptionDetails = e.ToDetails(),
                };

                exceptionInfo = ExceptionDispatchInfo.Capture(e);
            }
            finally
            {
                completedMessage.ParameterLogs = parameterLogCollector;
                completedMessage.EndTime = DateTimeOffset.UtcNow;
                _sharedContext.FunctionInstanceLogger.LogFunctionCompleted(completedMessage);
            }

            return new FunctionInvocationResult
            {
                Id = completedMessage.FunctionInstanceId,
                Succeeded = completedMessage.Succeeded,
                ExceptionInfo = exceptionInfo != null ? new ExceptionDispatchInfoDelayedException(exceptionInfo) : null
            };
        }

        private void ExecuteWithLogMessage(IFunctionInstance instance, RuntimeBindingProviderContext context,
            FunctionStartedMessage message, IDictionary<string, ParameterLog> parameterLogCollector)
        {
            // Create the console output writer
            FunctionOutputLog functionOutput = _sharedContext.OutputLogDispenser.CreateLogStream(instance);
            TextWriter consoleOutput = functionOutput.Output;
            context.ConsoleOutput = consoleOutput;

            // Must bind before logging (bound invoke string is included in log message).
            IReadOnlyDictionary<string, IValueProvider> parameters = instance.BindCommand.Execute();

            using (ValueProviderDisposable.Create(parameters))
            {
                LogFunctionStarted(message, functionOutput, parameters);

                try
                {
                    ExecuteWithOutputLogs(instance, parameters, consoleOutput, functionOutput.ParameterLogBlob,
                        parameterLogCollector);
                }
                catch (Exception exception)
                {
                    consoleOutput.WriteLine("--------");
                    consoleOutput.WriteLine("Exception while executing:");
                    consoleOutput.Write(exception.ToDetails());
                    throw;
                }
                finally
                {
                    functionOutput.CloseOutput();
                }
            }
        }

        private void LogFunctionStarted(FunctionStartedMessage message, FunctionOutputLog functionOutput,
            IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            // Finish populating the function started snapshot.
            message.OutputBlob = GetBlobDescriptor(functionOutput.Blob);
            message.ParameterLogBlob = GetBlobDescriptor(functionOutput.ParameterLogBlob);
            message.Arguments = CreateArguments(parameters);

            // Log that the function started.
            _sharedContext.FunctionInstanceLogger.LogFunctionStarted(message);
        }

        private static LocalBlobDescriptor GetBlobDescriptor(ICloudBlob blob)
        {
            if (blob == null)
            {
                return null;
            }

            return new LocalBlobDescriptor
            {
                ContainerName = blob.Container.Name,
                BlobName = blob.Name
            };
        }

        private void ExecuteWithOutputLogs(IFunctionInstance instance,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput,
            CloudBlockBlob parameterLogBlob, IDictionary<string, ParameterLog> parameterLogCollector)
        {
            MethodInfo method = instance.Method;
            ParameterInfo[] parameterInfos = method.GetParameters();
            IReadOnlyDictionary<string, ISelfWatch> watches = CreateWatches(parameters);
            SelfWatch selfWatch = CreateSelfWatch(watches, parameterLogBlob, consoleOutput);

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

                SelfWatch.AddLogs(watches, parameterLogCollector);
            }
        }

        private static IReadOnlyDictionary<string, ISelfWatch> CreateWatches(
            IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            Dictionary<string, ISelfWatch> watches = new Dictionary<string, ISelfWatch>();

            foreach (KeyValuePair<string, IValueProvider> item in parameters)
            {
                IWatchable watchable = item.Value as IWatchable;

                if (watchable != null)
                {
                    watches.Add(item.Key, watchable.Watcher);
                }
            }

            return watches;
        }

        private static SelfWatch CreateSelfWatch(IReadOnlyDictionary<string, ISelfWatch> watches,
            CloudBlockBlob parameterLogBlob, TextWriter consoleOutput)
        {
            if (parameterLogBlob == null)
            {
                return null;
            }

            return new SelfWatch(watches, parameterLogBlob, consoleOutput);
        }

        internal static void ExecuteWithSelfWatch(MethodInfo method, ParameterInfo[] parameterInfos,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput)
        {
            IDelayedException delayedBindingException;
            object[] reflectionParameters = PrepareParameters(parameterInfos, parameters, out delayedBindingException);

            if (delayedBindingException != null)
            {
                // This is done inside a self watch context so that each binding error is publish next to the binding in
                // the self watch log.
                delayedBindingException.Throw();
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

        private static object[] PrepareParameters(ParameterInfo[] parameterInfos,
            IReadOnlyDictionary<string, IValueProvider> parameters, out IDelayedException delayedBindingException)
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
                delayedBindingException = null;
            }
            else if (bindingExceptions.Count == 1)
            {
                delayedBindingException = new DelayedException(bindingExceptions[0]);
            }
            else
            {
                delayedBindingException = new DelayedException(new AggregateException(bindingExceptions));
            }

            return reflectionParameters;
        }

        private FunctionStartedMessage CreateStartedMessageWithoutArguments(IFunctionInstance instance,
            CloudStorageAccount storageAccount, string serviceBusConnectionString)
        {
            return new FunctionStartedMessage
            {
                HostInstanceId = _sharedContext.HostOutputMessage.HostInstanceId,
                HostDisplayName = _sharedContext.HostOutputMessage.HostDisplayName,
                SharedQueueName = _sharedContext.HostOutputMessage.SharedQueueName,
                InstanceQueueName = _sharedContext.HostOutputMessage.InstanceQueueName,
                Heartbeat = _sharedContext.HostOutputMessage.Heartbeat,
                Credentials = _sharedContext.HostOutputMessage.Credentials,
                WebJobRunIdentifier = _sharedContext.HostOutputMessage.WebJobRunIdentifier,
                FunctionInstanceId = instance.Id,
                Function = instance.FunctionDescriptor,
                ParentId = instance.ParentId,
                Reason = instance.Reason,
                StartTime = DateTimeOffset.UtcNow
            };
        }

        private static FunctionCompletedMessage CreateCompletedMessage(FunctionStartedMessage startedMessage)
        {
            return new FunctionCompletedMessage
            {
                HostInstanceId = startedMessage.HostInstanceId,
                HostDisplayName = startedMessage.HostDisplayName,
                SharedQueueName = startedMessage.SharedQueueName,
                InstanceQueueName = startedMessage.InstanceQueueName,
                Heartbeat = startedMessage.Heartbeat,
                Credentials = startedMessage.Credentials,
                WebJobRunIdentifier = startedMessage.WebJobRunIdentifier,
                FunctionInstanceId = startedMessage.FunctionInstanceId,
                Function = startedMessage.Function,
                Arguments = startedMessage.Arguments,
                ParentId = startedMessage.ParentId,
                Reason = startedMessage.Reason,
                StartTime = startedMessage.StartTime,
                OutputBlob = startedMessage.OutputBlob,
                ParameterLogBlob = startedMessage.ParameterLogBlob
            };
        }

        private static IDictionary<string, string> CreateArguments(IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            IDictionary<string, string> arguments = new Dictionary<string, string>();

            if (parameters != null)
            {
                foreach (KeyValuePair<string, IValueProvider> parameter in parameters)
                {
                    arguments.Add(parameter.Key, parameter.Value.ToInvokeString());
                }
            }

            return arguments;
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
