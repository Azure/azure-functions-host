// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    internal class FileLogger : ILogger
    {
        private readonly IFileWriter _fileWriter;
        private readonly Func<bool> _isFileLoggingEnabled;
        private readonly Func<bool> _isPrimary;
        private readonly string _categoryName;
        private readonly LogType _logType;
        private readonly IExternalScopeProvider _scopeProvider;

        public FileLogger(string categoryName, IFileWriter fileWriter, Func<bool> isFileLoggingEnabled, Func<bool> isPrimary, LogType logType, IExternalScopeProvider scopeProvider)
        {
            _fileWriter = fileWriter;
            _isFileLoggingEnabled = isFileLoggingEnabled;
            _isPrimary = isPrimary;
            _categoryName = categoryName;
            _logType = logType;
            _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

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

            if (exception != null)
            {
                if (exception is FunctionInvocationException ||
                    exception is AggregateException)
                {
                    // we want to minimize the stack traces for function invocation
                    // failures, so we drill into the very inner exception, which will
                    // be the script error
                    Exception actualException = exception;
                    while (actualException.InnerException != null)
                    {
                        actualException = actualException.InnerException;
                    }

                    formattedMessage += $"{Environment.NewLine}{actualException.Message}";
                }
                else
                {
                    formattedMessage += $"{Environment.NewLine}{exception.ToFormattedString()}";
                }
            }

            formattedMessage = FormatLine(stateValues, logLevel, formattedMessage);

            try
            {
                _fileWriter.AppendLine(formattedMessage);
            }
            catch (Exception ex) when (!ex.IsFatal())
            {
                // Make sure the Logger doesn't throw if there are Exceptions (disk full, etc).
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
