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
    [DebuggerDisplay(@"InnerFactory = {_inner}, ScriptHostState = {_manager.State}")]
    public sealed class ForwardingLoggerFactory : ILoggerFactory
    {
        private readonly ConcurrentDictionary<string, ForwardingLogger> _loggers = new(StringComparer.Ordinal);
        private readonly ILoggerFactory _inner;
        private readonly IScriptHostManager _manager;

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

            ForwardingLogger CreateLoggerImpl(string categoryName)
            {
                ILogger innerLogger = _inner.CreateLogger(categoryName);
                return new ForwardingLogger(categoryName, innerLogger, _manager);
            }

            return _loggers.GetOrAdd(categoryName, CreateLoggerImpl);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // this is just to block further logger creation.
            _disposed = true;
        }
    }
}
