// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// A logger that defers log entries to a channel.
    /// </summary>
    public class DeferredLogger : ILogger
    {
        private readonly Channel<DeferredLogEntry> _channel;
        private readonly string _categoryName;
        private IExternalScopeProvider _scopeProvider;

        public DeferredLogger(Channel<DeferredLogEntry> channel, string categoryName, IExternalScopeProvider scopeProvider)
        {
            _channel = channel;
            _categoryName = categoryName;
            _scopeProvider = scopeProvider;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Debug;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            string formattedMessage = formatter?.Invoke(state, exception);
            if (string.IsNullOrEmpty(formattedMessage))
            {
                return;
            }

            var log = new DeferredLogEntry
            {
                LogLevel = logLevel,
                Category = _categoryName,
                Message = formattedMessage,
                Exception = exception,
                EventId = eventId
            };

            IList<string> stringScope = null;
            _scopeProvider.ForEachScope((scope, _) =>
            {
                if (scope is IEnumerable<KeyValuePair<string, object>> kvps)
                {
                    log.ScopeCollection = log.ScopeCollection ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

                    foreach (var kvp in kvps)
                    {
                        // ToString to ignore any context.
                        log.ScopeCollection[kvp.Key] = kvp.Value.ToString();
                    }
                }
                else if (scope is string stringValue && !string.IsNullOrEmpty(stringValue))
                {
                    stringScope = stringScope ?? new List<string>();
                    stringScope.Add(stringValue);
                }
            }, (object)null);

            if (stringScope != null)
            {
                log.ScopeCollection = log.ScopeCollection ?? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                log.ScopeCollection.Add("Scope", string.Join(" => ", stringScope));
            }

            _channel.Writer.TryWrite(log);
        }
    }
}