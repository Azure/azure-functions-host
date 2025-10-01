// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Azure.WebJobs.Script;

#nullable enable

namespace Microsoft.Extensions.Logging
{
    /// <summary>
    /// A logger factory that creates loggers which track the current active ScriptHost (if any), falling
    /// back to the WebHost logger if no ScriptHost is active.
    /// </summary>
    [DebuggerDisplay(@"InnerFactory = \{ {_inner} \}, ScriptHostState = {_manager.State}")]
    public sealed class ForwardingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentDictionary<string, ForwardingLogger> _loggers = new(StringComparer.Ordinal);
        private readonly ILoggerFactory _inner;
        private readonly IScriptHostManager _manager;
        private readonly object _sync = new();

        private bool _disposed;

        public ForwardingLoggerFactory(ILoggerFactory inner, IScriptHostManager manager)
        {
            ArgumentNullException.ThrowIfNull(inner);
            ArgumentNullException.ThrowIfNull(manager);
            _inner = inner;
            _manager = manager;
        }

        /// <inheritdoc />
        public void AddProvider(ILoggerProvider provider)
            => throw new NotSupportedException(
                $"{nameof(ILoggerProvider)} can not be added to the {nameof(ForwardingLoggerFactory)}.");

        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_loggers.TryGetValue(categoryName, out ForwardingLogger? logger))
            {
                lock (_sync)
                {
                    if (!_loggers.TryGetValue(categoryName, out logger))
                    {
                        ILogger innerLogger = _inner.CreateLogger(categoryName);
                        logger = new ForwardingLogger(categoryName, innerLogger, _manager);
                        _loggers[categoryName] = logger;
                    }
                }
            }

            return logger;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // this is just to block further logger creation.
            _disposed = true;
        }
    }
}
