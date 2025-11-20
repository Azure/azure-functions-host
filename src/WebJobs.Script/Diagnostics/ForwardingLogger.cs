// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Extensions.DependencyInjection;

#nullable enable

namespace Microsoft.Extensions.Logging
{
    internal sealed class ForwardingLogger : ILogger
    {
        // The service key to use for dependency injection to get forwarding loggers.
        public const string ServiceKey = "Forwarding";

        private readonly string _categoryName;
        private readonly ILogger _fallback;
        private readonly IScriptHostManager _manager;

        // We use weak references so as to not keep a ScriptHost alive after it shuts down.
        private readonly WeakReference<ILogger> _current = new(null!);
        private readonly WeakReference<IServiceProvider> _services = new(null!);

        public ForwardingLogger(string categoryName, ILogger inner, IScriptHostManager manager)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(manager);
            _categoryName = categoryName;
            _fallback = inner;
            _manager = manager;
        }

        private ILogger Current
        {
            get
            {
                if (TryGetCurrentLogger(out ILogger? logger))
                {
                    return logger;
                }

                // No current ScriptHost logger, or the ScriptHost is gone. Use the fallback WebHost logger.
                return _fallback;
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
            => Current.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => Current.IsEnabled(logLevel);

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Current.Log(logLevel, eventId, state, exception, formatter);

        private bool TryGetCurrentLogger([NotNullWhen(true)] out ILogger? logger)
        {
            if (IsLoggerCurrent(out logger))
            {
                return true;
            }
            else if (_manager.Services is { } services)
            {
                logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(_categoryName);
                _services.SetTarget(services);
                _current.SetTarget(logger);
                return true;
            }

            logger = null;
            return false;
        }

        private bool IsLoggerCurrent([NotNullWhen(true)] out ILogger? logger)
        {
            // First check if the last IServiceProvider we used is still active.
            if (_services.TryGetTarget(out IServiceProvider? services)
                && ReferenceEquals(services, _manager.Services))
            {
                // Service provider is still correct, so our logger is current.
                return _current.TryGetTarget(out logger);
            }

            logger = null;
            return false;
        }
    }

    [DebuggerDisplay("{_logger}")]
    internal sealed class ForwardingLogger<T> : ILogger<T>
    {
        private readonly ILogger _logger;

        public ForwardingLogger([ForwardingLogger] ILoggerFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);
            _logger = factory.CreateLogger<T>();
        }

        IDisposable? ILogger.BeginScope<TState>(TState state) => _logger.BeginScope(state);

        bool ILogger.IsEnabled(LogLevel logLevel) => _logger.IsEnabled(logLevel);

        void ILogger.Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            _logger.Log(logLevel, eventId, state, exception, formatter);
    }
}
