// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.Functions.WorkerProxy.Diagnostics;

internal sealed class WorkerProxyFileLoggerProvider : ILoggerProvider
{
    internal const string DefaultLogFilePath = "/home/logs.log";

    private readonly Action<string> _writeLine;
    private readonly IDisposable? _disposable;

    public WorkerProxyFileLoggerProvider()
        : this(new FileLineWriter(DefaultLogFilePath))
    {
    }

    internal WorkerProxyFileLoggerProvider(string logFilePath)
        : this(new FileLineWriter(logFilePath))
    {
    }

    internal WorkerProxyFileLoggerProvider(Action<string> writeLine)
    {
        ArgumentNullException.ThrowIfNull(writeLine);

        _writeLine = writeLine;
    }

    private WorkerProxyFileLoggerProvider(FileLineWriter lineWriter)
        : this(lineWriter.WriteLine)
    {
        _disposable = lineWriter;
    }

    public ILogger CreateLogger(string categoryName)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        return new WorkerProxyFileLogger(categoryName, _writeLine);
    }

    public void Dispose()
    {
        _disposable?.Dispose();
    }

    private sealed class FileLineWriter : IDisposable
    {
        private readonly object _syncRoot = new();
        private readonly StreamWriter _writer;

        public FileLineWriter(string logFilePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

            string? directory = Path.GetDirectoryName(logFilePath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var stream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream)
            {
                AutoFlush = true
            };
        }

        public void WriteLine(string message)
        {
            ArgumentNullException.ThrowIfNull(message);

            lock (_syncRoot)
            {
                _writer.WriteLine(message);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                _writer.Dispose();
            }
        }
    }
}
