// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Provides a logger for writing all host logs to a single Host file.
    /// </summary>
    internal class HostFileLoggerProvider : ILoggerProvider
    {
        private readonly FileWriter _writer;
        private readonly Func<bool> _isFileLoggingEnabled;

        private bool _disposed = false;

        public HostFileLoggerProvider(string rootLogPath, Func<bool> isFileLoggingEnabled)
        {
            _writer = new FileWriter(Path.Combine(rootLogPath, "Host"));
            _isFileLoggingEnabled = isFileLoggingEnabled;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new FileLogger(categoryName, _writer, _isFileLoggingEnabled, () => true);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _writer.Dispose();
                _disposed = true;
            }
        }
    }
}
