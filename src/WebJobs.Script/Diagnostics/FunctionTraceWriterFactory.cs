// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class FunctionTraceWriterFactory : IFunctionTraceWriterFactory
    {
        private readonly ConcurrentDictionary<string, TraceWriter> _writerCache = new ConcurrentDictionary<string, TraceWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly ScriptHostConfiguration _scriptHostConfig;
        private readonly Func<TraceEvent, bool> _fileLoggingEnabled;

        public FunctionTraceWriterFactory(ScriptHostConfiguration scriptHostConfig, Func<TraceEvent, bool> fileLoggingEnabled)
        {
            _scriptHostConfig = scriptHostConfig;
            _fileLoggingEnabled = fileLoggingEnabled;
        }

        public TraceWriter Create(string functionName, string logDirName = null)
        {
            logDirName = logDirName ?? "Function";

            return _writerCache.GetOrAdd(functionName, f => CreateTraceWriter(f, logDirName));
        }

        private TraceWriter CreateTraceWriter(string functionName, string dirName)
        {
            TraceLevel functionTraceLevel = _scriptHostConfig.HostConfig.Tracing.ConsoleLevel;

            // File logging is done conditionally.
            string logFilePath = Path.Combine(_scriptHostConfig.RootLogPath, dirName, functionName);
            var fileTraceWriter = new FileTraceWriter(logFilePath, functionTraceLevel, LogType.Function)
                .Conditional(_fileLoggingEnabled);

            // Set up a TraceWriter for logging the count of user logs written.
            IMetricsLogger metricsLogger = _scriptHostConfig.HostConfig.GetService<IMetricsLogger>();
            var userLogMetricsTraceWriter = new UserLogMetricsTraceWriter(metricsLogger, functionName, functionTraceLevel);

            var innerTraceWriter = new CompositeTraceWriter(new TraceWriter[] { fileTraceWriter, userLogMetricsTraceWriter }, functionTraceLevel);

            // Wrap in a RemovableTraceWriter so we can remove it from the cache when it is disposed
            return new RemovableTraceWriter(this, functionName, innerTraceWriter);
        }

        private void RemoveTraceWriter(string functionName)
        {
            _writerCache.TryRemove(functionName, out TraceWriter unused);
        }

        internal class RemovableTraceWriter : TraceWriter, IDisposable
        {
            private readonly TraceWriter _innerWriter;
            private readonly FunctionTraceWriterFactory _parentFactory;
            private readonly string _functionName;
            private bool _disposed = false;

            public RemovableTraceWriter(FunctionTraceWriterFactory parentFactory, string functionName, TraceWriter innerWriter)
                : base(innerWriter.Level)
            {
                _parentFactory = parentFactory;
                _innerWriter = innerWriter;
                _functionName = functionName;
            }

            public override void Trace(TraceEvent traceEvent)
            {
                _innerWriter.Trace(traceEvent);
            }

            public override void Flush()
            {
                _innerWriter.Flush();

                base.Flush();
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _parentFactory.RemoveTraceWriter(_functionName);
                    (_innerWriter as IDisposable)?.Dispose();
                    _disposed = true;
                }
            }
        }
    }
}