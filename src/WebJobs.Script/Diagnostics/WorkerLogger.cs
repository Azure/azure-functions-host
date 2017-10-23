// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// A wrapper class to allow ILoggers from functions to write to the existing FileTraceWriter. This allows
    /// logs to show up in the log files and stream in the portal.
    /// </summary>
    internal class WorkerLogger : ILogger
    {
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly string _categoryName;
        private readonly string _language;
        private readonly string _workerId;
        private readonly IFunctionTraceWriterFactory _traceWriterFactory;

        public WorkerLogger(string categoryName, IFunctionTraceWriterFactory traceWriterFactory, Func<string, LogLevel, bool> filter)
        {
            _categoryName = categoryName;
            _filter = filter;
            _traceWriterFactory = traceWriterFactory;
            var parts = categoryName.Split('.');
            _language = parts[1];
            _workerId = parts[2];
        }

        public static Regex Regex { get; } = new Regex(@"^Worker\.[^\s]+\.[^\s]+");

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
                !IsEnabled(logLevel))
            {
                return;
            }

            TraceEvent traceEvent = new TraceEvent(logLevel.ToTraceLevel(), formattedMessage, _categoryName, exception);

            TraceWriter traceWriter = _traceWriterFactory.Create(_language, "Worker");
            traceWriter.Trace(traceEvent);
        }
    }
}
