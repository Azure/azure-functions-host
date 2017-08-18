// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script
{
    public sealed class FunctionTraceWriterFactory : IFunctionTraceWriterFactory
    {
        private readonly ConcurrentDictionary<string, TraceWriter> _writerCache = new ConcurrentDictionary<string, TraceWriter>(StringComparer.OrdinalIgnoreCase);
        private readonly ScriptHostConfiguration _scriptHostConfig;

        public FunctionTraceWriterFactory(ScriptHostConfiguration scriptHostConfig)
        {
            _scriptHostConfig = scriptHostConfig;
        }

        public TraceWriter Create(string functionName, string logDirName = null)
        {
            logDirName = logDirName ?? "Function";
            if (_scriptHostConfig.FileLoggingMode != FileLoggingMode.Never)
            {
                return _writerCache.GetOrAdd(functionName, f => CreateTraceWriter(_scriptHostConfig, f, logDirName));
            }

            return NullTraceWriter.Instance;
        }

        private TraceWriter CreateTraceWriter(ScriptHostConfiguration config, string functionName, string dirName)
        {
            TraceLevel functionTraceLevel = config.HostConfig.Tracing.ConsoleLevel;
            string logFilePath = Path.Combine(config.RootLogPath, dirName, functionName);

            // Wrap the FileTraceWriter in a RemovableTraceWriter so we can remove it from the cache when it is disposed
            var innerTraceWriter = new FileTraceWriter(logFilePath, functionTraceLevel);
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