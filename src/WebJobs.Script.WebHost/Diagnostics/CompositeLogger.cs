// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics
{
    internal sealed class CompositeLogger : ILogger
    {
        private readonly ILogger[] _loggers;

        public CompositeLogger(params ILogger[] loggers)
        {
            _loggers = loggers is { Length: > 0 } ? loggers : throw new ArgumentNullException(nameof(loggers));
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            // Create a composite scope that wraps scopes from all loggers
            var scopes = new IDisposable[_loggers.Length];
            for (int i = 0; i < _loggers.Length; i++)
            {
                scopes[i] = _loggers[i].BeginScope(state);
            }

            return new CompositeScope(scopes);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _loggers.Any(l => l.IsEnabled(logLevel));
        }

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            foreach (var logger in _loggers)
            {
                if (logger.IsEnabled(logLevel))
                {
                    logger.Log(logLevel, eventId, state, exception, formatter);
                }
            }
        }

        private sealed class CompositeScope : IDisposable
        {
            private readonly IDisposable[] _scopes;

            public CompositeScope(IDisposable[] scopes)
                => _scopes = scopes;

            public void Dispose()
            {
                foreach (ref readonly var scope in _scopes.AsSpan())
                {
                    scope?.Dispose();
                }
            }
        }
    }
}