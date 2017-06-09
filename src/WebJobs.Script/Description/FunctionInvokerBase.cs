// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private bool _disposed = false;
        private IMetricsLogger _metrics;
        private IDisposable _fileChangeSubscription;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata)
        {
            Host = host;
            Metadata = functionMetadata;
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();

            // Function file logging is only done conditionally
            TraceWriter traceWriter = host.FunctionTraceWriterFactory.Create(functionMetadata.Name);
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

        internal TraceWriter FileTraceWriter { get; set; }

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
                string functionBasePath = Path.GetDirectoryName(Metadata.ScriptFile) + Path.DirectorySeparatorChar;
                _fileChangeSubscription = Host.EventManager.OfType<FileEvent>()
                    .Where(f => string.Equals(f.Source, EventSources.ScriptFiles, StringComparison.Ordinal) &&
                    f.FileChangeArguments.FullPath.StartsWith(functionBasePath, StringComparison.OrdinalIgnoreCase))
                    .Subscribe(e => OnScriptFileChanged(e.FileChangeArguments));

                return true;
            }

            return false;
        }

        public async Task Invoke(object[] parameters)
        {
            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();
            PopulateExecutionContext(functionExecutionContext);

            // These may not be present, so null is okay.
            TraceWriter functionTraceWriter = parameters.OfType<TraceWriter>().FirstOrDefault();
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();
            ILogger logger = parameters.OfType<ILogger>().FirstOrDefault();
            string invocationId = functionExecutionContext.InvocationId.ToString();

            var startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
            _metrics.BeginEvent(startedEvent);
            var invokeLatencyEvent = LogInvocationMetrics(_metrics, Metadata);
            var invocationStopWatch = new Stopwatch();
            invocationStopWatch.Start();

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

                invocationStopWatch.Stop();
                LogFunctionResult(startedEvent, true, invocationId, invocationStopWatch.ElapsedMilliseconds);
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

                invocationStopWatch.Stop();
                LogFunctionResult(startedEvent, false, invocationId, invocationStopWatch.ElapsedMilliseconds);
                exInfo.Throw();
            }
            catch
            {
                invocationStopWatch.Stop();
                LogFunctionResult(startedEvent, false, invocationId, invocationStopWatch.ElapsedMilliseconds);
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

        private void LogFunctionResult(FunctionStartedEvent startedEvent, bool success, string invocationId, long elapsedMs)
        {
            if (startedEvent != null)
            {
                startedEvent.Success = success;
            }

            string resultString = success ? "Success" : "Failure";
            string message = $"Function completed ({resultString}, Id={invocationId ?? "0"}, Duration={elapsedMs}ms)";

            TraceLevel traceWriterLevel = success ? TraceLevel.Info : TraceLevel.Error;
            LogLevel logLevel = success ? LogLevel.Information : LogLevel.Error;

            TraceWriter.Trace(message, traceWriterLevel, null);
            Logger?.Log(logLevel, new EventId(0), message, null, (s, e) => s);
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

        protected virtual void OnScriptFileChanged(FileSystemEventArgs e)
        {
        }

        protected void TraceOnPrimaryHost(string message, TraceLevel level)
        {
            TraceWriter.Trace(message, level, PrimaryHostTraceProperties);
        }

        protected void PopulateExecutionContext(ExecutionContext context)
        {
            context.FunctionDirectory = Metadata.FunctionDirectory;
            context.FunctionName = Metadata.Name;
        }

        internal void TraceCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics, LogTargets logTarget = LogTargets.All)
        {
            if (logTarget == LogTargets.None)
            {
                return;
            }

            TraceWriter traceWriter = TraceWriter;
            IDictionary<string, object> properties = PrimaryHostTraceProperties;

            if (!logTarget.HasFlag(LogTargets.User))
            {
                traceWriter = Host.TraceWriter;
                properties = PrimaryHostSystemTraceProperties;
            }
            else if (!logTarget.HasFlag(LogTargets.System))
            {
                properties = PrimaryHostUserTraceProperties;
            }

            foreach (var diagnostic in diagnostics.Where(d => !d.IsSuppressed))
            {
                traceWriter.Trace(diagnostic.ToString(), diagnostic.Severity.ToTraceLevel(), properties);
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileChangeSubscription?.Dispose();

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
