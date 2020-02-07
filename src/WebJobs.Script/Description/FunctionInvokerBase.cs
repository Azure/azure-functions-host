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
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private bool _disposed = false;
        private IDisposable _fileChangeSubscription;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ILoggerFactory loggerFactory, string logDirName = null)
        {
            Host = host;
            Metadata = functionMetadata;
            FunctionLogger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory(functionMetadata.Name));
        }

        protected static IDictionary<string, object> PrimaryHostLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object> { { ScriptConstants.LogPropertyPrimaryHostKey, true } });

        protected static IDictionary<string, object> PrimaryHostUserLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostLogProperties) { { ScriptConstants.LogPropertyIsUserLogKey, true } });

        protected static IDictionary<string, object> PrimaryHostSystemLogProperties { get; }
            = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(PrimaryHostLogProperties) { { ScriptConstants.LogPropertyIsSystemLogKey, true } });

        public ScriptHost Host { get; }

        public ILogger FunctionLogger { get; }

        public FunctionMetadata Metadata { get; }

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
            FunctionLogger.LogError(errorMessage);
        }

        protected bool InitializeFileWatcherIfEnabled()
        {
            if (Host.ScriptOptions.FileWatchingEnabled)
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
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();
            ILogger logger = parameters.OfType<ILogger>().FirstOrDefault();

            FunctionInvocationContext context = new FunctionInvocationContext
            {
                ExecutionContext = functionExecutionContext,
                Binder = binder,
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
                metrics.LogEvent(eventName, metadata.Name);
            }

            return metrics.BeginEvent(MetricEventNames.FunctionInvokeLatency, metadata.Name);
        }

        protected abstract Task<object> InvokeCore(object[] parameters, FunctionInvocationContext context);

        protected virtual void OnScriptFileChanged(FileSystemEventArgs e)
        {
        }

        protected internal void LogOnPrimaryHost(string message, LogLevel level, Exception exception = null)
        {
            IDictionary<string, object> properties = new Dictionary<string, object>(PrimaryHostLogProperties);

            FunctionLogger.Log(level, 0, properties, exception, (state, ex) => message);
        }

        internal void TraceCompilationDiagnostics(ImmutableArray<Diagnostic> diagnostics, LogTargets logTarget = LogTargets.All, bool isInvocation = false)
        {
            if (logTarget == LogTargets.None)
            {
                return;
            }

            // build the log state based on inputs
            Dictionary<string, object> logState = new Dictionary<string, object>();
            if (!isInvocation)
            {
                // generally we only want to trace compilation diagnostics on the single primary
                // host, to avoid duplicate log statements in the case of file save operations.
                // however if the function is being invoked, we always want to output detailed
                // information.
                logState.Add(ScriptConstants.LogPropertyPrimaryHostKey, true);
            }
            if (!logTarget.HasFlag(LogTargets.User))
            {
                logState.Add(ScriptConstants.LogPropertyIsSystemLogKey, true);
            }
            else if (!logTarget.HasFlag(LogTargets.System))
            {
                logState.Add(ScriptConstants.LogPropertyIsUserLogKey, true);
            }

            // log the diagnostics
            foreach (var diagnostic in diagnostics.Where(d => !d.IsSuppressed))
            {
                FunctionLogger.Log(diagnostic.Severity.ToLogLevel(), 0, logState, null, (s, e) => diagnostic.ToString());
            }

            // log structured logs
            if (Host.InDebugMode && (Host.IsPrimary || isInvocation))
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
        private readonly FunctionMetadata _metadata;
        private readonly IMetricsLogger _metrics;
        private readonly Guid _invocationId;

        private FunctionStartedEvent _startedEvent;
        private object _invokeLatencyEvent;

        public FunctionInstanceMonitor(
            FunctionMetadata metadata,
            IMetricsLogger metrics,
            Guid invocationId)
        {
            _metadata = metadata;
            _metrics = metrics;
            _invocationId = invocationId;
        }

        public void Start()
        {
            _startedEvent = new FunctionStartedEvent(_invocationId, _metadata);
            _metrics.BeginEvent(_startedEvent);

            _invokeLatencyEvent = FunctionInvokerBase.LogInvocationMetrics(_metrics, _metadata);
        }

        // Called on success and failure
        public void End(bool success)
        {
            _startedEvent.Success = success;

            var data = new JObject
            {
                ["Language"] = _startedEvent.FunctionMetadata.Language,
                ["FunctionName"] = _metadata != null ? _metadata.Name : string.Empty,
                ["Success"] = success,
                ["IsStopwatchHighResolution"] = Stopwatch.IsHighResolution
            };
            string jsonData = data.ToString();
            _startedEvent.Data = jsonData;
            string eventName = success ? MetricEventNames.FunctionInvokeSucceeded : MetricEventNames.FunctionInvokeFailed;
            _metrics.LogEvent(eventName, _startedEvent.FunctionName, jsonData);

            _metrics.EndEvent(_startedEvent);

            if (_invokeLatencyEvent is MetricEvent metricEvent)
            {
                metricEvent.Data = jsonData;
            }

            _metrics.EndEvent(_invokeLatencyEvent);
        }
    }
}
