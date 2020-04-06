// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Provides a logger for writing all host logs to a single Host file.
    /// </summary>
    internal class HostFileLoggerProvider : ILoggerProvider, ISupportExternalScope
    {
        private readonly IFileWriter _writer;
        private readonly Func<bool> _isFileLoggingEnabled;

        private bool _disposed = false;
        private IExternalScopeProvider _scopeProvider;

        public HostFileLoggerProvider(IOptions<ScriptJobHostOptions> options, IFileLoggingStatusManager fileLoggingStatusManager, IFileWriterFactory fileWriterFactory)
        {
            if (fileWriterFactory == null)
            {
                throw new ArgumentNullException(nameof(fileWriterFactory));
            }

            _writer = fileWriterFactory.Create(Path.Combine(options.Value.RootLogPath, "Host"));
            _isFileLoggingEnabled = () => fileLoggingStatusManager.IsFileLoggingEnabled;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, _writer, _isFileLoggingEnabled, () => true, LogType.Host, _scopeProvider);
        }

        public void SetScopeProvider(IExternalScopeProvider scopeProvider)
        {
            _scopeProvider = scopeProvider;
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                (_writer as IDisposable)?.Dispose();
                _disposed = true;
            }
        }
    }
}
