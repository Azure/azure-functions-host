// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class FileLogger : ILogger
    {
        private readonly FileWriter _fileWriter;
        private readonly Func<bool> _isFileLoggingEnabled;
        private readonly Func<bool> _isPrimary;
        private readonly string _categoryName;
        private readonly LogType _logType;

        public FileLogger(string categoryName, FileWriter fileWriter, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary, LogType logType)
        {
            _fileWriter = fileWriter;
            _isFileLoggingEnabled = isFileLoggingEnabled;
            _isPrimary = isPrimary;
            _categoryName = categoryName;
            _logType = logType;
        }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            return _isFileLoggingEnabled();
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var stateValues = state as IEnumerable<KeyValuePair<string, object>>;
            string formattedMessage = formatter?.Invoke(state, exception);

            // If we don't have a message, there's nothing to log.
            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            bool isSystemTrace = Utility.GetStateBoolValue(stateValues, ScriptConstants.LogPropertyIsSystemLogKey);
            if (isSystemTrace)
            {
                // System traces are not logged to files.
                return;
            }

            bool isPrimaryHostTrace = Utility.GetStateBoolValue(stateValues, ScriptConstants.LogPropertyPrimaryHostKey);
            if (isPrimaryHostTrace && !_isPrimary())
            {
                return;
            }

            formattedMessage = FormatLine(stateValues, logLevel, formattedMessage);
            _fileWriter.AppendLine(formattedMessage);

            // flush errors immediately
            if (logLevel == LogLevel.Error || exception != null)
            {
                _fileWriter.Flush();
            }
        }

        /// <summary>
        /// Format the log line for the current event being traced.
        /// </summary>
        /// <param name="stateValues">The event state.</param>
        /// <param name="level">The event level.</param>
        /// <param name="line">The log line to format.</param>
        /// <returns>The formatted log message.</returns>
        protected virtual string FormatLine(IEnumerable<KeyValuePair<string, object>> stateValues, LogLevel level, string line)
        {
            string tracePrefix = GetLogPrefix(stateValues, level, _logType);
            string timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fff", CultureInfo.InvariantCulture);
            string formattedLine = $"{timestamp} [{tracePrefix}] {line.Trim()}";

            return formattedLine;
        }

        internal static string GetLogPrefix(IEnumerable<KeyValuePair<string, object>> stateValues, LogLevel level, LogType logType)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(level.ToString());

            if (logType == LogType.Host)
            {
                var functionName = Utility.GetStateValueOrDefault<string>(stateValues, ScriptConstants.LogPropertyFunctionNameKey);
                if (!string.IsNullOrEmpty(functionName))
                {
                    sb.AppendFormat(",{0}", functionName);
                }
            }

            return sb.ToString();
        }
    }
}
