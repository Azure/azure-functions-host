// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class TestLogger<T> : TestLogger, ILogger<T>
    {
        public TestLogger() : base(typeof(T).Name) { }
    }

    [DebuggerDisplay("{DebuggerDisplay,nq}")]
    public class TestLogger : ILogger
    {
        private readonly object _syncLock = new object();
        private readonly IExternalScopeProvider _scopeProvider;
        private readonly IList<LogMessage> _logMessages = new List<LogMessage>();
        private readonly ITestOutputHelper _testOutput; // optionally write direct to the test output

        public TestLogger(string category, string hostInstanceId = null, IExternalScopeProvider scopeProvider = null, ITestOutputHelper testOutput = null)
        {
            Category = category;
            HostInstanceId = hostInstanceId;
            _scopeProvider = scopeProvider ?? new LoggerExternalScopeProvider();
            _testOutput = testOutput;
        }

        public string Category { get; private set; }

        public string HostInstanceId { get; private set; }

        private string DebuggerDisplay => $"Category: {Category}, Count: {_logMessages.Count}";

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

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
            LogMessage logMessage = new LogMessage
            {
                Level = logLevel,
                EventId = eventId,
                State = state as IEnumerable<KeyValuePair<string, object>>,
                Scope = _scopeProvider.GetScopeDictionaryOrNull(),
                Exception = exception,
                FormattedMessage = formatter(state, exception),
                Category = Category,
                Timestamp = DateTime.UtcNow,
                HostInstanceId = HostInstanceId
            };

            lock (_syncLock)
            {
                _logMessages.Add(logMessage);
            }

            _testOutput?.WriteLine($"{logMessage}");

            // Uncomment this when debugging test failures in CI.
            // Console.WriteLine($"{logMessage}");
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

        public string HostInstanceId { get; set; }

        public override string ToString()
        {
            string hostInstance = string.IsNullOrEmpty(HostInstanceId) ? string.Empty : $" [{HostInstanceId}]";

            return $"[{Timestamp:HH:mm:ss.fff}]{hostInstance} [{Level}] [{Category}] {FormattedMessage} {Exception}";
        }
    }
}