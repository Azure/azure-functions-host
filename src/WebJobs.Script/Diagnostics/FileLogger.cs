// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly string _categoryName;
        private readonly IFunctionTraceWriterFactory _traceWriterFactory;

        public FileLogger(string categoryName, IFunctionTraceWriterFactory traceWriterFactory, Func<string, LogLevel, bool> filter)
        {
            _categoryName = categoryName;
            _filter = filter;
            _traceWriterFactory = traceWriterFactory;
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
            IEnumerable<KeyValuePair<string, object>> properties = state as IEnumerable<KeyValuePair<string, object>>;
            string formattedMessage = formatter?.Invoke(state, exception);

            // If we have no structured data and no message, there's nothing to log
            if ((string.IsNullOrEmpty(formattedMessage) && properties == null) ||
                !IsEnabled(logLevel) || IsFromTraceWriter(properties))
            {
                return;
            }

            TraceLevel traceLevel = GetTraceLevel(logLevel);
            TraceEvent traceEvent = new TraceEvent(traceLevel, formattedMessage, _categoryName, exception);
            string functionName = GetFunctionName();

            // If we don't have a function name, we have no way to create a TraceWriter
            if (functionName == null)
            {
                return;
            }

            TraceWriter traceWriter = _traceWriterFactory.Create(functionName);
            traceWriter.Trace(traceEvent);
        }

        private static string GetFunctionName()
        {
            IDictionary<string, object> scopeProperties = DictionaryLoggerScope.GetMergedStateDictionary();

            if (!scopeProperties.TryGetValue(ScriptConstants.LoggerFunctionNameKey, out string functionName))
            {
                return null;
            }

            // this function name starts with "Functions.", but file paths do not include this
            string functionsPrefix = "Functions.";
            if (functionName.StartsWith(functionsPrefix))
            {
                functionName = functionName.Substring(functionsPrefix.Length);
            }

            return functionName;
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
            if (properties == null)
            {
                return false;
            }
            else
            {
                return properties.Any(kvp => string.Equals(kvp.Key, ScriptConstants.TracePropertyIsUserTraceKey, StringComparison.OrdinalIgnoreCase));
            }
        }
    }
}
