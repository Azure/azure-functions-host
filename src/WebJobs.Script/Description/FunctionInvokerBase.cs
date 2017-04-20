﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private readonly Stopwatch _stopwatch = new Stopwatch();
        private AutoRecoveringFileSystemWatcher _fileWatcher;
        private bool _disposed = false;
        private IMetricsLogger _metrics;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ITraceWriterFactory traceWriterFactory = null)
        {
            Host = host;
            Metadata = functionMetadata;
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();

            // Function file logging is only done conditionally
            traceWriterFactory = traceWriterFactory ?? new FunctionTraceWriterFactory(functionMetadata.Name, Host.ScriptConfig);
            TraceWriter traceWriter = traceWriterFactory.Create();
            FileTraceWriter = traceWriter.Conditional(t => Host.FileLoggingEnabled && (!(t.Properties?.ContainsKey(ScriptConstants.TracePropertyPrimaryHostKey) ?? false) || Host.IsPrimary));

            // The global trace writer used by the invoker will write all traces to both
            // the host trace writer as well as our file trace writer
            TraceWriter = host.TraceWriter != null ?
                new CompositeTraceWriter(new TraceWriter[] { FileTraceWriter, host.TraceWriter }) :
                FileTraceWriter;

            // Apply the function name as an event property to all traces
            var functionTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyFunctionNameKey, Metadata.Name }
            };
            TraceWriter = TraceWriter.Apply(functionTraceProperties);

            Logger = host.ScriptConfig.HostConfig.LoggerFactory?.CreateLogger(LogCategories.Executor);
        }

        protected static IDictionary<string, object> PrimaryHostTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { { ScriptConstants.TracePropertyPrimaryHostKey, true } });

        protected static IDictionary<string, object> PrimaryHostUserTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostTraceProperties) { { ScriptConstants.TracePropertyIsUserTraceKey, true } });

        protected static IDictionary<string, object> PrimaryHostSystemTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostTraceProperties) { { ScriptConstants.TracePropertyIsSystemTraceKey, true } });

        public ScriptHost Host { get; }

        public FunctionMetadata Metadata { get; }

        private TraceWriter FileTraceWriter { get; set; }

        public TraceWriter TraceWriter { get; }

        public ILogger Logger { get; }

        /// <summary>
        /// All unhandled invocation exceptions will flow through this method.
        /// We format the error and write it to our function specific <see cref="TraceWriter"/>.
        /// </summary>
        /// <param name="ex">The exception instance.</param>
        public virtual void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex);

            TraceError(error);
        }

        protected virtual void TraceError(string errorMessage)
        {
            TraceWriter.Error(errorMessage);
            Logger?.LogError(errorMessage);

            // when any errors occur, we want to flush immediately
            TraceWriter.Flush();
        }

        protected bool InitializeFileWatcherIfEnabled()
        {
            if (Host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(Metadata.ScriptFile);
                _fileWatcher = new AutoRecoveringFileSystemWatcher(functionDirectory);
                _fileWatcher.Changed += OnScriptFileChanged;

                return true;
            }

            return false;
        }

        public async Task Invoke(object[] parameters)
        {
            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();

            // These may not be present, so null is okay.
            TraceWriter functionTraceWriter = parameters.OfType<TraceWriter>().FirstOrDefault();
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();
            ILogger logger = parameters.OfType<ILogger>().FirstOrDefault();
            string invocationId = functionExecutionContext.InvocationId.ToString();

            var startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
            _metrics.BeginEvent(startedEvent);
            var invokeLatencyEvent = LogInvocationMetrics(_metrics, Metadata);
            _stopwatch.Restart();

            try
            {
                string startMessage = $"Function started (Id={invocationId})";
                TraceWriter.Info(startMessage);
                Logger?.LogInformation(startMessage);

                FunctionInvocationContext context = new FunctionInvocationContext
                {
                    ExecutionContext = functionExecutionContext,
                    Binder = binder,
                    TraceWriter = functionTraceWriter,
                    Logger = logger
                };

                await InvokeCore(parameters, context);

                _stopwatch.Stop();

                string completeMessage = $"Function completed (Success, Id={invocationId}, Duration={_stopwatch.ElapsedMilliseconds}ms)";
                TraceWriter.Info(completeMessage);
                Logger?.LogInformation(completeMessage);
            }
            catch (AggregateException ex)
            {
                ExceptionDispatchInfo exInfo = null;

                // If there's only a single exception, rethrow it by itself
                Exception singleEx = ex.Flatten().InnerExceptions.SingleOrDefault();
                if (singleEx != null)
                {
                    exInfo = ExceptionDispatchInfo.Capture(singleEx);
                }
                else
                {
                    exInfo = ExceptionDispatchInfo.Capture(ex);
                }

                _stopwatch.Stop();
                LogFunctionFailed(startedEvent, "Failure", invocationId, _stopwatch.ElapsedMilliseconds);
                exInfo.Throw();
            }
            catch
            {
                _stopwatch.Stop();
                LogFunctionFailed(startedEvent, "Failure", invocationId, _stopwatch.ElapsedMilliseconds);
                throw;
            }
            finally
            {
                if (startedEvent != null)
                {
                    _metrics.EndEvent(startedEvent);
                }
                if (invokeLatencyEvent != null)
                {
                    _metrics.EndEvent(invokeLatencyEvent);
                }
            }
        }

        internal static object LogInvocationMetrics(IMetricsLogger metrics, FunctionMetadata metadata)
        {
            // log events for each of the binding types used
            foreach (var binding in metadata.Bindings)
            {
                string eventName = binding.IsTrigger ?
                    string.Format(MetricEventNames.FunctionBindingTypeFormat, binding.Type) :
                    string.Format(MetricEventNames.FunctionBindingTypeDirectionFormat, binding.Type, binding.Direction);
                metrics.LogEvent(eventName);
            }

            return metrics.BeginEvent(MetricEventNames.FunctionInvokeLatency, metadata.Name);
        }

        private void LogFunctionFailed(FunctionStartedEvent startedEvent, string resultString, string invocationId, long elapsedMs)
        {
            if (startedEvent != null)
            {
                startedEvent.Success = false;
            }

            string message = $"Function completed ({resultString}, Id={invocationId ?? "0"}, Duration={elapsedMs}ms)";
            TraceWriter.Error(message);
            Logger?.LogError(message);
        }

        protected TraceWriter CreateUserTraceWriter(TraceWriter traceWriter)
        {
            // We create a composite writer to ensure that all user traces get
            // written to both the original trace writer as well as our file trace writer
            // This is a "user" trace writer that will mark all traces going through
            // it as a "user" trace so they are filtered from system logs.
            var userTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyIsUserTraceKey, true }
            };
            return new CompositeTraceWriter(new[] { traceWriter, FileTraceWriter }).Apply(userTraceProperties);
        }

        protected abstract Task InvokeCore(object[] parameters, FunctionInvocationContext context);

        protected virtual void OnScriptFileChanged(object sender, FileSystemEventArgs e)
        {
        }

        protected void TraceOnPrimaryHost(string message, TraceLevel level)
        {
            TraceWriter.Trace(message, level, PrimaryHostTraceProperties);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileWatcher?.Dispose();

                    (TraceWriter as IDisposable)?.Dispose();
                }

                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
