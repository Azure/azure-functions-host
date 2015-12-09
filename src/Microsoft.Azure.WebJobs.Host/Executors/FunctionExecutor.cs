// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Bindings;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Protocols;
using Microsoft.Azure.WebJobs.Host.Timers;

namespace Microsoft.Azure.WebJobs.Host.Executors
{
    // In-memory executor. 
    internal class FunctionExecutor : IFunctionExecutor
    {
        private readonly IFunctionInstanceLogger _functionInstanceLogger;
        private readonly IFunctionOutputLogger _functionOutputLogger;
        private readonly IBackgroundExceptionDispatcher _backgroundExceptionDispatcher;
        private readonly TimeSpan? _functionTimeout;
        private readonly TraceWriter _trace;

        private HostOutputMessage _hostOutputMessage;

        public FunctionExecutor(IFunctionInstanceLogger functionInstanceLogger, IFunctionOutputLogger functionOutputLogger, 
            IBackgroundExceptionDispatcher backgroundExceptionDispatcher, TraceWriter trace, TimeSpan? functionTimeout)
        {
            if (functionInstanceLogger == null)
            {
                throw new ArgumentNullException("functionInstanceLogger");
            }

            if (functionOutputLogger == null)
            {
                throw new ArgumentNullException("functionOutputLogger");
            }

            if (backgroundExceptionDispatcher == null)
            {
                throw new ArgumentNullException("backgroundExceptionDispatcher");
            }

            if (trace == null)
            {
                throw new ArgumentNullException("trace");
            }

            _functionInstanceLogger = functionInstanceLogger;
            _functionOutputLogger = functionOutputLogger;
            _backgroundExceptionDispatcher = backgroundExceptionDispatcher;
            _trace = trace;
            _functionTimeout = functionTimeout;
        }

        public HostOutputMessage HostOutputMessage
        {
            get { return _hostOutputMessage; }
            set { _hostOutputMessage = value; }
        }

        public async Task<IDelayedException> TryExecuteAsync(IFunctionInstance functionInstance, CancellationToken cancellationToken)
        {
            FunctionStartedMessage functionStartedMessage = CreateStartedMessageWithoutArguments(functionInstance);
            IDictionary<string, ParameterLog> parameterLogCollector = new Dictionary<string, ParameterLog>();
            FunctionCompletedMessage functionCompletedMessage = null;
            ExceptionDispatchInfo exceptionInfo = null;
            string functionStartedMessageId = null;
            TraceLevel functionTraceLevel = GetFunctionTraceLevel(functionInstance);

            try
            {
                functionStartedMessageId = await ExecuteWithLoggingAsync(functionInstance, functionStartedMessage, parameterLogCollector, functionTraceLevel, cancellationToken);
                functionCompletedMessage = CreateCompletedMessage(functionStartedMessage);
            }
            catch (Exception exception)
            {
                if (functionCompletedMessage == null)
                {
                    functionCompletedMessage = CreateCompletedMessage(functionStartedMessage);
                }

                functionCompletedMessage.Failure = new FunctionFailure
                {
                    Exception = exception,
                    ExceptionType = exception.GetType().FullName,
                    ExceptionDetails = exception.ToDetails(),
                };

                exceptionInfo = ExceptionDispatchInfo.Capture(exception);
            }

            if (functionCompletedMessage != null)
            {
                functionCompletedMessage.ParameterLogs = parameterLogCollector;
                functionCompletedMessage.EndTime = DateTimeOffset.UtcNow;
            }

            bool loggedStartedEvent = functionStartedMessageId != null;
            CancellationToken logCompletedCancellationToken;
            if (loggedStartedEvent)
            {
                // If function started was logged, don't cancel calls to log function completed.
                logCompletedCancellationToken = CancellationToken.None;
            }
            else
            {
                logCompletedCancellationToken = cancellationToken;
            }

            if (functionCompletedMessage != null &&
                ((functionTraceLevel >= TraceLevel.Info) || (functionCompletedMessage.Failure != null && functionTraceLevel >= TraceLevel.Error)))
            {
                await _functionInstanceLogger.LogFunctionCompletedAsync(functionCompletedMessage, logCompletedCancellationToken);
            }

            if (loggedStartedEvent)
            {
                await _functionInstanceLogger.DeleteLogFunctionStartedAsync(functionStartedMessageId, cancellationToken);
            }

            return exceptionInfo != null ? new ExceptionDispatchInfoDelayedException(exceptionInfo) : null;
        }

        internal static TraceLevel GetFunctionTraceLevel(IFunctionInstance functionInstance)
        {
            TraceLevel functionTraceLevel = TraceLevel.Verbose;

            // Determine the TraceLevel for this function (affecting both Console as well as Dashboard logging)
            // Note that for manual invocations (e.g. from Dashboard or via HostCall) we ignore the function logging
            // specification.
            if (functionInstance.Reason == ExecutionReason.AutomaticTrigger)
            {
                TraceLevelAttribute attribute = TypeUtility.GetHierarchicalAttributeOrNull<TraceLevelAttribute>(functionInstance.FunctionDescriptor.Method);
                if (attribute != null)
                {
                    functionTraceLevel = attribute.Level;
                }
            }

            return functionTraceLevel;
        }

        private async Task<string> ExecuteWithLoggingAsync(IFunctionInstance instance, FunctionStartedMessage message, 
            IDictionary<string, ParameterLog> parameterLogCollector, TraceLevel functionTraceLevel, CancellationToken cancellationToken)
        {
            IFunctionOutputDefinition outputDefinition = null;
            IFunctionOutput outputLog = null;
            ITaskSeriesTimer updateOutputLogTimer = null;
            TextWriter functionOutputTextWriter = null;

            Func<Task> initializeOutputAsync = async () =>
            {
                outputDefinition = await _functionOutputLogger.CreateAsync(instance, cancellationToken);
                outputLog = outputDefinition.CreateOutput();
                functionOutputTextWriter = outputLog.Output;
                updateOutputLogTimer = StartOutputTimer(outputLog.UpdateCommand, _backgroundExceptionDispatcher);
            };

            if (functionTraceLevel >= TraceLevel.Info)
            {
                await initializeOutputAsync();
            }

            try
            {
                // Create a linked token source that will allow us to signal function cancellation
                // (e.g. Based on TimeoutAttribute, etc.)
                CancellationTokenSource functionCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                using (functionCancellationTokenSource)
                {
                    // We create a new composite trace writer that will also forward
                    // output to the function output log (in addition to console, user TraceWriter, etc.).
                    TraceWriter traceWriter = new CompositeTraceWriter(_trace, functionOutputTextWriter);

                    // Must bind before logging (bound invoke string is included in log message).
                    FunctionBindingContext functionContext = new FunctionBindingContext(instance.Id, functionCancellationTokenSource.Token, traceWriter);
                    var valueBindingContext = new ValueBindingContext(functionContext, cancellationToken);
                    var parameters = await instance.BindingSource.BindAsync(valueBindingContext);

                    ExceptionDispatchInfo exceptionInfo = null;
                    string startedMessageId = null;
                    using (ValueProviderDisposable.Create(parameters))
                    {
                        if (functionTraceLevel >= TraceLevel.Info)
                        {
                            startedMessageId = await LogFunctionStartedAsync(message, outputDefinition, parameters, cancellationToken);
                        }
                        
                        try
                        {
                            await ExecuteWithLoggingAsync(instance, parameters, traceWriter, outputDefinition, parameterLogCollector, functionTraceLevel, functionCancellationTokenSource);
                        }
                        catch (Exception exception)
                        {
                            if (outputDefinition == null)
                            {
                                // In error cases, even if logging is disabled for this function, we want to force
                                // log errors. So we must delay initialize logging here
                                await initializeOutputAsync();
                                startedMessageId = await LogFunctionStartedAsync(message, outputDefinition, parameters, cancellationToken);
                            }

                            if (exception is OperationCanceledException)
                            {
                                exceptionInfo = ExceptionDispatchInfo.Capture(exception);
                            }
                            else
                            {
                                string errorMessage = string.Format("Exception while executing function: {0}", instance.FunctionDescriptor.ShortName);
                                FunctionInvocationException functionException = new FunctionInvocationException(errorMessage, instance.Id, instance.FunctionDescriptor.FullName, exception);
                                traceWriter.Error(errorMessage, functionException, TraceSource.Execution);
                                exceptionInfo = ExceptionDispatchInfo.Capture(functionException);
                            }
                        }
                    }

                    if (exceptionInfo == null && updateOutputLogTimer != null)
                    {
                        await updateOutputLogTimer.StopAsync(cancellationToken);
                    }

                    // after all execution is complete, flush the TraceWriter
                    traceWriter.Flush();

                    // We save the exception info above rather than throwing to ensure we always write
                    // console output even if the function fails or was canceled.
                    if (outputLog != null)
                    {
                        await outputLog.SaveAndCloseAsync(cancellationToken);
                    }

                    if (exceptionInfo != null)
                    {
                        // release any held singleton lock immediately
                        SingletonLock singleton = null;
                        if (TryGetSingletonLock(parameters, out singleton) && singleton.IsHeld)
                        {
                            await singleton.ReleaseAsync(cancellationToken);
                        }

                        exceptionInfo.Throw();
                    }

                    return startedMessageId;
                }
            }
            finally
            {
                if (outputLog != null)
                {
                    ((IDisposable)outputLog).Dispose();
                }
                if (updateOutputLogTimer != null)
                {
                    ((IDisposable)updateOutputLogTimer).Dispose();
                }
            }
        }

        /// <summary>
        /// If the specified function instance requires a timeout (either via <see cref="TimeoutAttribute"/>
        /// or because <see cref="JobHostConfiguration.FunctionTimeout"/> has been set, create and start the
        /// timer.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        internal static System.Timers.Timer StartFunctionTimeout(IFunctionInstance instance, TimeSpan? globalTimeout, CancellationTokenSource cancellationTokenSource, TraceWriter trace)
        {
            MethodInfo method = instance.FunctionDescriptor.Method;
            if (!method.GetParameters().Any(p => p.ParameterType == typeof(CancellationToken)))
            {
                // function doesn't bind to the CancellationToken, so no point in setting
                // up the cancellation timer
                return null;
            }

            // first see if there is a Timeout applied to the method or class
            TimeSpan? timeout = globalTimeout;
            TimeoutAttribute timeoutAttribute = TypeUtility.GetHierarchicalAttributeOrNull<TimeoutAttribute>(method);
            if (timeoutAttribute != null)
            {
                timeout = timeoutAttribute.Timeout;
            }

            if (timeout != null)
            {
                // Create a Timer that will cancel the token source when it fires. We're using our
                // own Timer (rather than CancellationToken.CancelAfter) so we can write a log entry
                // before cancellation occurs.
                var timer = new System.Timers.Timer(timeout.Value.TotalMilliseconds)
                {
                    AutoReset = false
                };
                timer.Elapsed += (o, e) => 
                {
                    OnFunctionTimeout(timer, method, instance.Id, timeout.Value, trace, cancellationTokenSource);
                };
                timer.Start();

                return timer;
            }

            return null;
        }

        internal static void OnFunctionTimeout(System.Timers.Timer timer, MethodInfo method, Guid instanceId, 
            TimeSpan timeout, TraceWriter trace, CancellationTokenSource cancellationTokenSource)
        {
            timer.Stop();

            string message = string.Format(CultureInfo.InvariantCulture,
                "Timeout value of {0} exceeded by function '{1}.{2}' (Id: '{3}'). Initiating cancellation.",
                timeout.ToString(), method.DeclaringType.Name, method.Name, instanceId);
            trace.Error(message, null, TraceSource.Execution);

            trace.Flush();

            // only cancel the token AFTER we've logged our error, since
            // the Dashboard function output is also tied to this cancellation
            // token and we don't want to dispose the logger prematurely.
            cancellationTokenSource.Cancel();
        }

        private Task<string> LogFunctionStartedAsync(FunctionStartedMessage message,
            IFunctionOutputDefinition functionOutput,
            IReadOnlyDictionary<string, IValueProvider> parameters,
            CancellationToken cancellationToken)
        {
            // Finish populating the function started snapshot.
            message.OutputBlob = functionOutput.OutputBlob;
            message.ParameterLogBlob = functionOutput.ParameterLogBlob;
            message.Arguments = CreateArguments(parameters);

            // Log that the function started.
            return _functionInstanceLogger.LogFunctionStartedAsync(message, cancellationToken);
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static ITaskSeriesTimer StartOutputTimer(IRecurrentCommand updateCommand, IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            if (updateCommand == null)
            {
                return null;
            }

            TimeSpan initialDelay = FunctionOutputIntervals.InitialDelay;
            TimeSpan refreshRate = FunctionOutputIntervals.RefreshRate;
            ITaskSeriesTimer timer = FixedDelayStrategy.CreateTimer(updateCommand, initialDelay, refreshRate, backgroundExceptionDispatcher);
            timer.Start();

            return timer;
        }

        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
        private static ITaskSeriesTimer StartParameterLogTimer(IRecurrentCommand updateCommand, IBackgroundExceptionDispatcher backgroundExceptionDispatcher)
        {
            if (updateCommand == null)
            {
                return null;
            }

            TimeSpan initialDelay = FunctionParameterLogIntervals.InitialDelay;
            TimeSpan refreshRate = FunctionParameterLogIntervals.RefreshRate;
            ITaskSeriesTimer timer = FixedDelayStrategy.CreateTimer(updateCommand, initialDelay, refreshRate, backgroundExceptionDispatcher);
            timer.Start();

            return timer;
        }

        private async Task ExecuteWithLoggingAsync(IFunctionInstance instance,
            IReadOnlyDictionary<string, IValueProvider> parameters,
            TraceWriter trace,
            IFunctionOutputDefinition outputDefinition,
            IDictionary<string, ParameterLog> parameterLogCollector,
            TraceLevel functionTraceLevel,
            CancellationTokenSource functionCancellationTokenSource)
        {
            IFunctionInvoker invoker = instance.Invoker;

            IReadOnlyDictionary<string, IWatcher> parameterWatchers = null;
            ITaskSeriesTimer updateParameterLogTimer = null;
            if (functionTraceLevel >= TraceLevel.Info)
            {
                parameterWatchers = CreateParameterWatchers(parameters);
                IRecurrentCommand updateParameterLogCommand = outputDefinition.CreateParameterLogUpdateCommand(parameterWatchers, trace);
                updateParameterLogTimer = StartParameterLogTimer(updateParameterLogCommand, _backgroundExceptionDispatcher);
            }
            
            try
            {
                await ExecuteWithWatchersAsync(instance, parameters, _functionTimeout, trace, functionCancellationTokenSource);
                    
                if (updateParameterLogTimer != null)
                {
                    // Stop the watches after calling IValueBinder.SetValue (it may do things that should show up in
                    // the watches).
                    // Also, IValueBinder.SetValue could also take a long time (flushing large caches), and so it's
                    // useful to have watches still running.
                    await updateParameterLogTimer.StopAsync(functionCancellationTokenSource.Token);
                }
            }
            finally
            {
                if (updateParameterLogTimer != null)
                {
                    ((IDisposable)updateParameterLogTimer).Dispose();
                }

                if (parameterWatchers != null)
                {
                    ValueWatcher.AddLogs(parameterWatchers, parameterLogCollector);
                }
            }
        }

        private static IReadOnlyDictionary<string, IWatcher> CreateParameterWatchers(IReadOnlyDictionary<string, IValueProvider> parameters)
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

        internal static async Task ExecuteWithWatchersAsync(IFunctionInstance instance,
            IReadOnlyDictionary<string, IValueProvider> parameters,
            TimeSpan? globalFunctionTimeout,
            TraceWriter traceWriter,
            CancellationTokenSource functionCancellationTokenSource)
        {
            IFunctionInvoker invoker = instance.Invoker;
            IReadOnlyList<string> parameterNames = invoker.ParameterNames;
            IDelayedException delayedBindingException;

            object[] invokeParameters = PrepareParameters(parameterNames, parameters, out delayedBindingException);

            if (delayedBindingException != null)
            {
                // This is done inside a watcher context so that each binding error is publish next to the binding in
                // the parameter status log.
                delayedBindingException.Throw();
            }

            // if the function is a Singleton, aquire the lock
            SingletonLock singleton = null;
            if (TryGetSingletonLock(parameters, out singleton))
            {
                await singleton.AcquireAsync(functionCancellationTokenSource.Token);
            }

            var timer = StartFunctionTimeout(instance, globalFunctionTimeout, functionCancellationTokenSource, traceWriter);
            try
            {
                await invoker.InvokeAsync(invokeParameters);
            }
            finally
            {
                if (timer != null)
                {
                    timer.Stop();
                    timer.Dispose();
                }
            }

            // Process any out parameters and persist any pending values.
            // Ensure IValueBinder.SetValue is called in BindStepOrder. This ordering is particularly important for
            // ensuring queue outputs occur last. That way, all other function side-effects are guaranteed to have
            // occurred by the time messages are enqueued.
            string[] parameterNamesInBindOrder = SortParameterNamesInStepOrder(parameters);
            foreach (string name in parameterNamesInBindOrder)
            {
                IValueProvider provider = parameters[name];
                IValueBinder binder = provider as IValueBinder;

                if (binder != null)
                {
                    object argument = invokeParameters[GetParameterIndex(parameterNames, name)];

                    try
                    {
                        // This could do complex things that may fail. Catch the exception.
                        await binder.SetValueAsync(argument, functionCancellationTokenSource.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        string message = String.Format(CultureInfo.InvariantCulture,
                            "Error while handling parameter {0} after function returned:", name);
                        throw new InvalidOperationException(message, exception);
                    }
                }
            }

            if (singleton != null)
            {
                await singleton.ReleaseAsync(functionCancellationTokenSource.Token);
            }
        }

        private static bool TryGetSingletonLock(IReadOnlyDictionary<string, IValueProvider> parameters, out SingletonLock singleton)
        {
            IValueProvider singletonValueProvider = null;
            singleton = null;
            if (parameters.TryGetValue(SingletonValueProvider.SingletonParameterName, out singletonValueProvider))
            {
                singleton = (SingletonLock)singletonValueProvider.GetValue();
                return true;
            }

            return false;
        }

        private static object[] PrepareParameters(IReadOnlyList<string> parameterNames,
            IReadOnlyDictionary<string, IValueProvider> parameters, out IDelayedException delayedBindingException)
        {
            object[] reflectionParameters = new object[parameterNames.Count];
            List<Exception> bindingExceptions = new List<Exception>();

            for (int index = 0; index < parameterNames.Count; index++)
            {
                string name = parameterNames[index];
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
            FunctionStartedMessage message = new FunctionStartedMessage
            {
                HostInstanceId = _hostOutputMessage.HostInstanceId,
                HostDisplayName = _hostOutputMessage.HostDisplayName,
                SharedQueueName = _hostOutputMessage.SharedQueueName,
                InstanceQueueName = _hostOutputMessage.InstanceQueueName,
                Heartbeat = _hostOutputMessage.Heartbeat,
                WebJobRunIdentifier = _hostOutputMessage.WebJobRunIdentifier,
                FunctionInstanceId = instance.Id,
                Function = instance.FunctionDescriptor,
                ParentId = instance.ParentId,
                Reason = instance.Reason,
                StartTime = DateTimeOffset.UtcNow
            };

            // It's important that the host formats the reason before sending the message.
            // This enables extensibility scenarios. For the built in types, the Host and Dashboard
            // share types so it's possible (in the case of triggered functions) for the formatting
            // to require a call to TriggerParameterDescriptor.GetTriggerReason and that can only
            // be done on the Host side in the case of extensions (since the dashboard doesn't
            // know about extension types).
            message.ReasonDetails = message.FormatReason();

            return message;
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
                WebJobRunIdentifier = startedMessage.WebJobRunIdentifier,
                FunctionInstanceId = startedMessage.FunctionInstanceId,
                Function = startedMessage.Function,
                Arguments = startedMessage.Arguments,
                ParentId = startedMessage.ParentId,
                Reason = startedMessage.Reason,
                ReasonDetails = startedMessage.FormatReason(),
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

        private static int GetParameterIndex(IReadOnlyList<string> parameterNames, string name)
        {
            for (int index = 0; index < parameterNames.Count; index++)
            {
                if (parameterNames[index] == name)
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

        private class ValueBinderStepOrderComparer : IComparer<IValueProvider>
        {
            private static readonly ValueBinderStepOrderComparer Singleton = new ValueBinderStepOrderComparer();

            private ValueBinderStepOrderComparer()
            {
            }

            public static ValueBinderStepOrderComparer Instance 
            { 
                get 
                { 
                    return Singleton; 
                } 
            }

            public int Compare(IValueProvider x, IValueProvider y)
            {
                return Comparer<int>.Default.Compare((int)GetStepOrder(x), (int)GetStepOrder(y));
            }

            private static BindStepOrder GetStepOrder(IValueProvider provider)
            {
                IOrderedValueBinder orderedBinder = provider as IOrderedValueBinder;

                if (orderedBinder == null)
                {
                    return BindStepOrder.Default;
                }

                return orderedBinder.StepOrder;
            }
        }
    }
}
