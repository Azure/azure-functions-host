// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    internal class ScriptLogger<T> : ILogger<T>
    {
        private readonly ILogger<T> _logger;
        private readonly bool _isInternalLogger;

        internal static readonly IReadOnlyDictionary<string, object> SystemLogScope = new ReadOnlyDictionary<string, object>(new Dictionary<string, object>
        {
            { ScriptConstants.LogPropertyIsSystemLogKey, true }
        });

        public ScriptLogger(ILoggerFactory factory, ISystemAssemblyManager systemAssemblyManager)
        {
            if (factory == null)
            {
                throw new ArgumentNullException(nameof(factory));
            }

            if (systemAssemblyManager == null)
            {
                throw new ArgumentNullException(nameof(systemAssemblyManager));
            }

            _isInternalLogger = systemAssemblyManager.IsSystemAssembly(typeof(T).Assembly);
            _logger = new Logger<T>(factory);
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return _logger.BeginScope(state);
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return _logger.IsEnabled(logLevel);
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            using (BeginScopeIfInternal())
            {
                _logger.Log(logLevel, eventId, state, exception, formatter);
            }
        }

        private IDisposable BeginScopeIfInternal() => _isInternalLogger ? _logger.BeginScope(SystemLogScope) : null;
    }
}