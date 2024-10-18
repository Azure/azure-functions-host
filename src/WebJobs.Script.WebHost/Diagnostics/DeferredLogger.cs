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
        private readonly List<string> _scopes = new List<string>();

        public DeferredLogger(Channel<DeferredLogEntry> channel, string categoryName)
        {
            _channel = channel;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            string scopeData = state is string value ? value : string.Empty;

            if (!string.IsNullOrEmpty(scopeData))
            {
                _scopes.Add(scopeData);
            }

            // Return IDisposable to remove scope from active scopes when done
            return new ScopeRemover(() => _scopes.Remove(scopeData));
        }

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
                EventId = eventId,
                Scope = string.Join(", ", _scopes)
            };
            _channel.Writer.TryWrite(log);
        }
    }

    public class ScopeRemover : IDisposable
    {
        private readonly Action _onDispose;

        public ScopeRemover(Action onDispose)
        {
            _onDispose = onDispose;
        }

        public void Dispose()
        {
            _onDispose();
        }
    }
}