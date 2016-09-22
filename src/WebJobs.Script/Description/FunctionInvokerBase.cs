﻿// Copyright (c) .NET Foundation. All rights reserved.
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
        private FileSystemWatcher _fileWatcher;
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

            PrimaryHostTraceProperties = new Dictionary<string, object>
            {
                { ScriptConstants.TracePropertyPrimaryHostKey, true }
            };
        }

        protected IDictionary<string, object> PrimaryHostTraceProperties { get; }

        public ScriptHost Host { get; }

        public FunctionMetadata Metadata { get; }

        private TraceWriter FileTraceWriter { get; set; }

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
            // We require the ExecutionContext, so this will throw if one is not found.
            ExecutionContext functionExecutionContext = parameters.OfType<ExecutionContext>().First();

            // These may not be present, so null is okay.
            TraceWriter functionTraceWriter = parameters.OfType<TraceWriter>().FirstOrDefault();
            Binder binder = parameters.OfType<Binder>().FirstOrDefault();

            string invocationId = functionExecutionContext.InvocationId.ToString();

            FunctionStartedEvent startedEvent = new FunctionStartedEvent(functionExecutionContext.InvocationId, Metadata);
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
