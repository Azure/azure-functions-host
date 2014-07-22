// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.Jobs.Host.Bindings;
using Microsoft.Azure.Jobs.Host.Loggers;
using Microsoft.Azure.Jobs.Host.Protocols;
using Microsoft.Azure.Jobs.Host.Timers;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;

namespace Microsoft.Azure.Jobs.Host.Executors
{
    // In-memory executor. 
    class FunctionExecutor : IFunctionExecutor
    {
        private readonly FunctionExecutorContext _context;

        public FunctionExecutor(FunctionExecutorContext context)
        {
            _context = context;
        }

        public IDelayedException TryExecute(IFunctionInstance instance)
        {
            FunctionStartedMessage startedMessage = CreateStartedMessageWithoutArguments(instance);
            IDictionary<string, ParameterLog> parameterLogCollector = new Dictionary<string, ParameterLog>();
            FunctionCompletedMessage completedMessage = null;

            ExceptionDispatchInfo exceptionInfo = null;

            string startedMessageId = null;
            try
            {
                startedMessageId = ExecuteWithLogMessage(instance, startedMessage, parameterLogCollector);
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
                _context.FunctionInstanceLogger.LogFunctionCompleted(completedMessage);
                _context.FunctionInstanceLogger.DeleteLogFunctionStarted(startedMessageId);
            }

            return exceptionInfo != null ? new ExceptionDispatchInfoDelayedException(exceptionInfo) : null;
        }

        private string ExecuteWithLogMessage(IFunctionInstance instance, FunctionStartedMessage message,
            IDictionary<string, ParameterLog> parameterLogCollector)
        {
            string startedMessageId;

            // Create the console output writer
            IFunctionOutputDefinition outputDefinition = _context.FunctionOutputLogger.Create(instance);

            using (IFunctionOutput outputLog = outputDefinition.CreateOutput())
            using (IntervalSeparationTimer updateOutputLogTimer = StartOutputTimer(outputLog.UpdateCommand))
            {
                TextWriter consoleOutput = outputLog.Output;
                FunctionBindingContext functionContext =
                    new FunctionBindingContext(_context.BindingContext, instance.Id, consoleOutput);

                // Must bind before logging (bound invoke string is included in log message).
                IReadOnlyDictionary<string, IValueProvider> parameters = instance.BindingSource.Bind(functionContext);

                using (ValueProviderDisposable.Create(parameters))
                {
                    startedMessageId = LogFunctionStarted(message, outputDefinition, parameters);

                    try
                    {
                        ExecuteWithOutputLogs(instance, parameters, consoleOutput, outputDefinition,
                            parameterLogCollector);
                    }
                    catch (Exception exception)
                    {
                        consoleOutput.WriteLine("--------");
                        consoleOutput.WriteLine("Exception while executing:");
                        consoleOutput.Write(exception.ToDetails());
                        throw;
                    }
                }

                if (updateOutputLogTimer != null)
                {
                    updateOutputLogTimer.Stop();
                }

                outputLog.SaveAndClose();

                return startedMessageId;
            }
        }

        private string LogFunctionStarted(FunctionStartedMessage message, IFunctionOutputDefinition functionOutput,
            IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            // Finish populating the function started snapshot.
            message.OutputBlob = functionOutput.OutputBlob;
            message.ParameterLogBlob = functionOutput.ParameterLogBlob;
            message.Arguments = CreateArguments(parameters);

            // Log that the function started.
            return _context.FunctionInstanceLogger.LogFunctionStarted(message);
        }

        private static IntervalSeparationTimer StartOutputTimer(ICanFailCommand updateCommand)
        {
            if (updateCommand == null)
            {
                return null;
            }

            TimeSpan initialDelay = FunctionOutputIntervals.InitialDelay;
            TimeSpan refreshRate = FunctionOutputIntervals.RefreshRate;
            IntervalSeparationTimer timer =
                FixedIntervalsTimerCommand.CreateTimer(updateCommand, initialDelay, refreshRate);
            timer.Start(executeFirst: false);
            return timer;
        }

        private static IntervalSeparationTimer StartParameterLogTimer(ICanFailCommand updateCommand)
        {
            if (updateCommand == null)
            {
                return null;
            }

            TimeSpan initialDelay = FunctionParameterLogIntervals.InitialDelay;
            TimeSpan refreshRate = FunctionParameterLogIntervals.RefreshRate;
            IntervalSeparationTimer timer =
                FixedIntervalsTimerCommand.CreateTimer(updateCommand, initialDelay, refreshRate);
            timer.Start(executeFirst: false);
            return timer;
        }

        private void ExecuteWithOutputLogs(IFunctionInstance instance,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput,
            IFunctionOutputDefinition outputDefinition, IDictionary<string, ParameterLog> parameterLogCollector)
        {
            MethodInfo method = instance.Method;
            ParameterInfo[] parameterInfos = method.GetParameters();
            IReadOnlyDictionary<string, IWatcher> watches = CreateWatches(parameters);
            ICanFailCommand updateParameterLogCommand =
                outputDefinition.CreateParameterLogUpdateCommand(watches, consoleOutput);

            using (IntervalSeparationTimer updateParameterLogTimer = StartParameterLogTimer(updateParameterLogCommand))
            {
                try
                {
                    ExecuteWithWatchers(method, parameterInfos, parameters, consoleOutput);

                    if (updateParameterLogTimer != null)
                    {
                        // Stop the watches after calling IValueBinder.SetValue (it may do things that should show up in
                        // the watches).
                        // Also, IValueBinder.SetValue could also take a long time (flushing large caches), and so it's
                        // useful to have watches still running.
                        updateParameterLogTimer.Stop();
                    }
                }
                finally
                {
                    ValueWatcher.AddLogs(watches, parameterLogCollector);
                }
            }
        }

        private static IReadOnlyDictionary<string, IWatcher> CreateWatches(
            IReadOnlyDictionary<string, IValueProvider> parameters)
        {
            Dictionary<string, IWatcher> watches = new Dictionary<string, IWatcher>();

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

        private static ValueWatcher CreateValueWatcher(IReadOnlyDictionary<string, IWatcher> watches,
            CloudBlockBlob parameterLogBlob, TextWriter consoleOutput)
        {
            if (parameterLogBlob == null)
            {
                return null;
            }

            return new ValueWatcher(watches, parameterLogBlob, consoleOutput);
        }

        internal static void ExecuteWithWatchers(MethodInfo method, ParameterInfo[] parameterInfos,
            IReadOnlyDictionary<string, IValueProvider> parameters, TextWriter consoleOutput)
        {
            IDelayedException delayedBindingException;
            object[] reflectionParameters = PrepareParameters(parameterInfos, parameters, out delayedBindingException);

            if (delayedBindingException != null)
            {
                // This is done inside a watcher context so that each binding error is publish next to the binding in
                // the parameter status log.
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

        private FunctionStartedMessage CreateStartedMessageWithoutArguments(IFunctionInstance instance)
        {
            return new FunctionStartedMessage
            {
                HostInstanceId = _context.HostOutputMessage.HostInstanceId,
                HostDisplayName = _context.HostOutputMessage.HostDisplayName,
                SharedQueueName = _context.HostOutputMessage.SharedQueueName,
                InstanceQueueName = _context.HostOutputMessage.InstanceQueueName,
                Heartbeat = _context.HostOutputMessage.Heartbeat,
                Credentials = _context.HostOutputMessage.Credentials,
                WebJobRunIdentifier = _context.HostOutputMessage.WebJobRunIdentifier,
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
