// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// A wrapper class to allow ILoggers from functions to write to the existing FileTraceWriter. This allows
    /// logs to show up in the log files and stream in the portal.
    /// </summary>
    internal class FileLogger : ILogger
    {
        private static readonly ConcurrentDictionary<string, TraceWriter> _writerCache = new ConcurrentDictionary<string, TraceWriter>();
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly string _categoryName;
        private readonly ScriptHostConfiguration _config;

        public FileLogger(string categoryName, ScriptHostConfiguration config, Func<string, LogLevel, bool> filter)
        {
            _categoryName = categoryName;
            _config = config;
            _filter = filter;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            if (_filter == null)
            {
                // if there is no filter, assume it is always enabled
                return true;
            }

            return _filter(_categoryName, logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // We don't support any other type of state.
            IEnumerable<KeyValuePair<string, object>> properties = state as IEnumerable<KeyValuePair<string, object>>;
            string formattedMessage = formatter?.Invoke(state, exception);

            if (string.IsNullOrEmpty(formattedMessage) || properties == null || !IsEnabled(logLevel) || IsFromTraceWriter(properties))
            {
                return;
            }

            IDictionary<string, object> scopeProperties = DictionaryLoggerScope.GetMergedStateDictionary();

            if (!scopeProperties.TryGetValue(ScriptConstants.LoggerFunctionNameKey, out string functionName))
            {
                // We have nowhere to write the file if we don't know the function name
                return;
            }

            // this function name starts with "Functions.", but file paths do not include this
            string functionsPrefix = "Functions.";
            if (functionName.StartsWith(functionsPrefix))
            {
                functionName = functionName.Substring(functionsPrefix.Length);
            }

            TraceWriter traceWriter = _writerCache.GetOrAdd(functionName, (n) => CreateFileTraceWriter(n, _config));
            TraceLevel traceLevel = GetTraceLevel(logLevel);
            TraceEvent traceEvent = new TraceEvent(traceLevel, formattedMessage, _categoryName, exception);

            traceWriter.Trace(traceEvent);
        }

        // For testing
        internal static void FlushAllTraceWriters()
        {
            foreach (TraceWriter writer in _writerCache.Values)
            {
                writer.Flush();
            }
        }

        private static TraceLevel GetTraceLevel(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    return TraceLevel.Verbose;
                case LogLevel.Information:
                    return TraceLevel.Info;
                case LogLevel.Warning:
                    return TraceLevel.Warning;
                case LogLevel.Error:
                case LogLevel.Critical:
                    return TraceLevel.Error;
                case LogLevel.None:
                    return TraceLevel.Off;
                default:
                    throw new InvalidOperationException();
            }
        }

        private static bool IsFromTraceWriter(IEnumerable<KeyValuePair<string, object>> properties)
        {
            return properties.Any(kvp => kvp.Key == ScriptConstants.TracePropertyIsUserTraceKey);
        }

        private static TraceWriter CreateFileTraceWriter(string functionName, ScriptHostConfiguration scriptHostConfig)
        {
            ITraceWriterFactory factory = new FunctionTraceWriterFactory(functionName, scriptHostConfig);
            return factory.Create();
        }
    }
}
