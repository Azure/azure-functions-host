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
        private readonly IExternalScopeProvider _scopeProvider;

        public DeferredLogger(Channel<DeferredLogEntry> channel, string categoryName, IExternalScopeProvider scopeProvider)
        {
            _channel = channel;
            _categoryName = categoryName;
            _scopeProvider = scopeProvider;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        // Restrict logging to errors only
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

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

            _scopeProvider.ForEachScope((scope, state) =>
            {
                state.ScopeStorage ??= new List<object>();
                state.ScopeStorage.Add(scope);
            }, log);

            _channel.Writer.TryWrite(log);
        }
    }
}