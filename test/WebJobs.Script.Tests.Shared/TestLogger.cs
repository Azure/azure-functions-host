﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class TestLogger : ILogger
    {
        private readonly Func<string, LogLevel, bool> _filter;
        private IList<LogMessage> _logMessages = new List<LogMessage>();

        // protect against changes to _logMessages while enumerating
        private object _syncLock = new object();

        public TestLogger(string category, Func<string, LogLevel, bool> filter = null)
        {
            Category = category;
            _filter = filter;
        }

        public string Category { get; private set; }

        public IDisposable BeginScope<TState>(TState state) => DictionaryLoggerScope.Push(state);

        public bool IsEnabled(LogLevel logLevel)
        {
            return _filter?.Invoke(Category, logLevel) ?? true;
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
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var logMessage = new LogMessage
            {
                Level = logLevel,
                EventId = eventId,
                State = state as IEnumerable<KeyValuePair<string, object>>,
                Scope = DictionaryLoggerScope.GetMergedStateDictionary(),
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

        public override string ToString() => $"[{Timestamp.ToString("HH:mm:ss.fff")}] [{Category}] {FormattedMessage}";
    }
}