// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Bindings.Runtime;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.Description
{
    [CLSCompliant(false)]
    public abstract class FunctionInvokerBase : IFunctionInvoker, IDisposable
    {
        private const string PrimaryHostTracePropertyName = "PrimaryHost";
        private readonly static IDictionary<string, object> _primaryHostTraceProperties = new Dictionary<string, object> { { PrimaryHostTracePropertyName, null } };

        private FileSystemWatcher _fileWatcher;
        private bool _disposed = false;
        private IMetricsLogger _metrics;

        internal FunctionInvokerBase(ScriptHost host, FunctionMetadata functionMetadata, ITraceWriterFactory traceWriterFactory = null)
        {
            Host = host;
            Metadata = functionMetadata;

            traceWriterFactory = traceWriterFactory ?? new FunctionTraceWriterFactory(functionMetadata.Name, Host.ScriptConfig);
            TraceWriter traceWriter = traceWriterFactory.Create();

            // Function file logging is only done conditionally
            TraceWriter = traceWriter.Conditional(t => Host.FileLoggingEnabled && (!(t.Properties?.ContainsKey(PrimaryHostTracePropertyName) ?? false) || Host.IsPrimary));
            _metrics = host.ScriptConfig.HostConfig.GetService<IMetricsLogger>();
        }

        protected static IDictionary<string, object> PrimaryHostTraceProperties => _primaryHostTraceProperties;

        public ScriptHost Host { get; private set; }

        public FunctionMetadata Metadata { get; private set; }

        public TraceWriter TraceWriter { get; }

        /// <summary>
        /// All unhandled invocation exceptions will flow through this method.
        /// We format the error and write it to our function specific <see cref="TraceWriter"/>.
        /// </summary>
        /// <param name="ex"></param>
        public virtual void OnError(Exception ex)
        {
            string error = Utility.FlattenException(ex);

            TraceError(error);
        }

        protected virtual void TraceError(string errorMessage)
        {
            TraceWriter.Error(errorMessage);

            // when any errors occur, we want to flush immediately
            TraceWriter.Flush();
        }

        protected bool InitializeFileWatcherIfEnabled()
        {
            if (Host.ScriptConfig.FileWatchingEnabled)
            {
                string functionDirectory = Path.GetDirectoryName(Metadata.ScriptFile);
                _fileWatcher = new FileSystemWatcher(functionDirectory, "*.*")
                {
                    IncludeSubdirectories = true,
                    EnableRaisingEvents = true
                };
                _fileWatcher.Changed += OnScriptFileChanged;
                _fileWatcher.Created += OnScriptFileChanged;
                _fileWatcher.Deleted += OnScriptFileChanged;
                _fileWatcher.Renamed += OnScriptFileChanged;

                return true;
            }

            return false;
        }

        public async Task Invoke(object[] parameters)
        {
            FunctionStartedEvent startedEvent = null;

            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();

            // These may not be present, so null is okay.
            TraceWriter functionTraceWriter = parameters.OfType<TraceWriter>().FirstOrDefault();
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();

            string invocationId = functionExecutionContext.InvocationId.ToString();

            startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
            _metrics.BeginEvent(startedEvent);

            try
            {
                TraceWriter.Info($"Function started (Id={invocationId})");

                FunctionInvocationContext context = new FunctionInvocationContext
                {
                    ExecutionContext = functionExecutionContext,
                    Binder = binder,
                    TraceWriter = functionTraceWriter
                };

                await InvokeCore(parameters, context);

                TraceWriter.Info($"Function completed (Success, Id={invocationId})");
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

                LogFunctionFailed(startedEvent, "Failure", invocationId);
                exInfo.Throw();
            }
            catch
            {
                LogFunctionFailed(startedEvent, "Failure", invocationId);
                throw;
            }
            finally
            {
                if (startedEvent != null)
                {
                    _metrics.EndEvent(startedEvent);
                }
            }
        }

        private void LogFunctionFailed(FunctionStartedEvent startedEvent, string resultString, string invocationId)
        {
            if (startedEvent != null)
            {
                startedEvent.Success = false;
            }

            TraceWriter.Error($"Function completed ({resultString}, Id={invocationId ?? "0"})");
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
