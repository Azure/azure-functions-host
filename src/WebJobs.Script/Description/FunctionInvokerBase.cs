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
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private bool _disposed = false;
        private IDisposable _fileChangeSubscription;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata)
            : this(host, functionMetadata, new FunctionLogger(host, functionMetadata.Name))
        {
        }

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, FunctionLogger logInfo)
        {
            Host = host;
            Metadata = functionMetadata;
            LogInfo = logInfo;
        }

        protected static IDictionary<string, object> PrimaryHostTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { { ScriptConstants.TracePropertyPrimaryHostKey, true } });

        protected static IDictionary<string, object> PrimaryHostUserTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostTraceProperties) { { ScriptConstants.TracePropertyIsUserTraceKey, true } });

        protected static IDictionary<string, object> PrimaryHostSystemTraceProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostTraceProperties) { { ScriptConstants.TracePropertyIsSystemTraceKey, true } });

        public ScriptHost Host { get; }

        public FunctionLogger LogInfo { get; }

        public FunctionMetadata Metadata { get; }

        protected TraceWriter TraceWriter => LogInfo.TraceWriter;

        protected ILogger Logger => LogInfo.Logger;

        public TraceWriter FileTraceWriter => LogInfo.FileTraceWriter;

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
            LogInfo.TraceError(errorMessage);
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

        public async Task<object> Invoke(object[] parameters)
        {
            FunctionInvocationContext context = GetContextFromParameters(parameters, Metadata);
            return await InvokeCore(parameters, context);
        }

        private static FunctionInvocationContext GetContextFromParameters(object[] parameters, FunctionMetadata metadata)
        {
            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();
            functionExecutionContext.FunctionDirectory = metadata.FunctionDirectory;
            functionExecutionContext.FunctionName = metadata.Name;

            // These may not be present, so null is okay.
            TraceWriter functionTraceWriter = parameters.OfType<TraceWriter>().FirstOrDefault();
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();
            ILogger logger = parameters.OfType<ILogger>().FirstOrDefault();

            FunctionInvocationContext context = new FunctionInvocationContext
            {
                ExecutionContext = functionExecutionContext,
                Binder = binder,
                TraceWriter = functionTraceWriter,
                Logger = logger
            };

            return context;
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

        protected abstract Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context);

        protected virtual void OnScriptFileChanged(FileSystemEventArgs e)
        {
        }

        protected internal void TraceOnPrimaryHost(string message, TraceLevel level, string source = null,  Exception exception = null)
        {
            var traceEvent = new TraceEvent(level, message, source, exception);
            foreach (var item in PrimaryHostTraceProperties)
            {
                traceEvent.Properties.Add(item);
            }
            TraceWriter.Trace(traceEvent);
        }

        internal void TraceCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics, LogTargets logTarget = LogTargets.All)
        {
            if (logTarget == LogTargets.None)
            {
                return;
            }

            TraceWriter traceWriter = LogInfo.TraceWriter;
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

            if (Host.InDebugMode && Host.IsPrimary)
            {
                Host.EventManager.Publish(new StructuredLogEntryEvent(() =>
                {
                    var logEntry = new StructuredLogEntry("codediagnostic");
                    logEntry.AddProperty("functionName", Metadata.Name);
                    logEntry.AddProperty("diagnostics", diagnostics.Select(d =>
                    {
                        FileLinePositionSpan span = d.Location.GetMappedLineSpan();
                        return new
                        {
                            code = d.Id,
                            message = d.GetMessage(),
                            source = Path.GetFileName(d.Location.SourceTree?.FilePath ?? span.Path ?? string.Empty),
                            severity = d.Severity,
                            startLineNumber = span.StartLinePosition.Line + 1,
                            startColumn = span.StartLinePosition.Character + 1,
                            endLine = span.EndLinePosition.Line + 1,
                            endColumn = span.EndLinePosition.Character + 1,
                        };
                    }));

                    return logEntry;
                }));
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _fileChangeSubscription?.Dispose();

                    (LogInfo.TraceWriter as IDisposable)?.Dispose();
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

    // Per function instance
    public class FunctionInstanceMonitor
    {
        // From ctor
        private readonly FunctionMetadata _metadata;
        private readonly IMetricsLogger _metrics;
        private readonly Guid _invocationId;
        private readonly FunctionLogger _logInfo;

        private readonly Stopwatch _invocationStopWatch = new Stopwatch();

        private FunctionStartedEvent startedEvent;
        private object invokeLatencyEvent;

        public FunctionInstanceMonitor(
            FunctionMetadata metadata,
            IMetricsLogger metrics,
            Guid invocationId,
            FunctionLogger logInfo)
        {
            _metadata = metadata;
            _metrics = metrics;
            _invocationId = invocationId;
            _logInfo = logInfo;
        }

        public void Start()
        {
            _logInfo.LogFunctionStart(_invocationId.ToString());

            startedEvent = new FunctionStartedEvent(_invocationId, _metadata);
            _metrics.BeginEvent(startedEvent);
            invokeLatencyEvent = FunctionInvokerBase.LogInvocationMetrics(_metrics, _metadata);
            _invocationStopWatch.Start();
        }

        // Called on success and failure
        public void End(bool success)
        {
            _logInfo.LogFunctionResult(success, _invocationId.ToString(), _invocationStopWatch.ElapsedMilliseconds);

            startedEvent.Success = success;
            _metrics.EndEvent(startedEvent);

            if (invokeLatencyEvent != null)
            {
                _metrics.EndEvent(invokeLatencyEvent);
            }
        }
    }
}
