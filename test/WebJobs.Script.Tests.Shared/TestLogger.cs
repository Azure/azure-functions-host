// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class TestLogger : ILogger
    {
        private readonly object _syncLock = new object();
        private readonly IExternalScopeProvider _scopeProvider;
        private IList<LogMessage> _logMessages = new List<LogMessage>();

        public TestLogger(string category)
            : this(category, new LoggerExternalScopeProvider())
        {
        }

        public TestLogger(string category, IExternalScopeProvider scopeProvider)
        {
            Category = category;
            _scopeProvider = scopeProvider;
        }

        public string Category { get; private set; }

        private string DebuggerDisplay => $"Category: {Category}, Count: {_logMessages.Count}";

        public IDisposable BeginScope<TState>(TState state)
        {
            return _scopeProvider.Push(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IList<LogMessage> GetLogMessages()
        {
            lock (_syncLock)
            {
                return _logMessages.ToList();
            }
        }

        public void ClearLogMessages()
        {
            lock (_syncLock)
            {
                _logMessages.Clear();
            }
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var scopes = _scopeProvider.GetScopeDictionary();

            LogMessage logMessage = new LogMessage
            {
                Level = logLevel,
                EventId = eventId,
                State = state as IEnumerable<KeyValuePair<string, object>>,
                Scope = scopes,
                Exception = exception,
                FormattedMessage = formatter(state, exception),
                Category = Category,
                Timestamp = DateTime.UtcNow
            };

            lock (_syncLock)
            {
                _logMessages.Add(logMessage);
            }
        }
    }

    public class LogMessage
    {
        public LogLevel Level { get; set; }

        public EventId EventId { get; set; }

        public IEnumerable<KeyValuePair<string, object>> State { get; set; }

        public IDictionary<string, object> Scope { get; set; }

        public Exception Exception { get; set; }

        public string FormattedMessage { get; set; }

        public string Category { get; set; }

        public DateTime Timestamp { get; set; }

        public override string ToString()
        {
            string s = $"[{Timestamp.ToString("HH:mm:ss.fff")}] [{Category}] {FormattedMessage}";

            if (Exception != null)
            {
                s += " | " + Exception.Message;
            }

            return s;
        }
    }
}