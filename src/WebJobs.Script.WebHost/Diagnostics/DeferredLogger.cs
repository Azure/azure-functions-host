// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    /// <summary>
    /// A logger that defers log entries to a shared <see cref="DeferredLogSource"/>.
    /// </summary>
    internal class DeferredLogger : ILogger
    {
        private readonly DeferredLogSource _source;
        private readonly string _categoryName;
        private readonly IExternalScopeProvider _scopeProvider;
        private readonly IEnvironment _environment;

        // Cached placeholder mode flag
        private bool _isPlaceholderModeDisabled = false;

        public DeferredLogger(DeferredLogSource source, string categoryName, IExternalScopeProvider scopeProvider, IEnvironment environment)
        {
            _source = source;
            _categoryName = categoryName;
            _scopeProvider = scopeProvider;
            _environment = environment;
        }

        public IDisposable BeginScope<TState>(TState state) => _scopeProvider.Push(state);

        // Restrict logging to errors only for now, as we are seeing a lot of unnecessary logs.
        // https://github.com/Azure/azure-functions-host/issues/10556
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            // Skip logging if it's not enabled or placeholder mode is enabled
            if (!IsEnabled(logLevel))
            {
                return;
            }

            // Only check IsPlaceholderModeEnabled if it hasn't been disabled
            if (!_isPlaceholderModeDisabled && _environment.IsPlaceholderModeEnabled())
            {
                return;
            }

            // Cache the fact that placeholder mode is disabled
            _isPlaceholderModeDisabled = true;

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

            // Persist the scope state so it can be reapplied in the original order when forwarding logs to the
            // logging provider.
            if (_scopeProvider is not null)
            {
                var scopeStorage = new List<object>();
                _scopeProvider.ForEachScope(static (scope, state) => state.Add(scope), scopeStorage);
                if (scopeStorage.Count > 0)
                {
                    log.ScopeStorage = scopeStorage;
                }
            }

            _source.Write(log);
        }
    }
}