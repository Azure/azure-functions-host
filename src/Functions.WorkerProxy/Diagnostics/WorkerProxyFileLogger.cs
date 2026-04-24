// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.Functions.WorkerProxy.Diagnostics;

internal sealed class WorkerProxyFileLogger : ILogger
{
    private const string TimestampFormat = "O";

    private readonly string _categoryName;
    private readonly Action<string> _writeLine;

    internal WorkerProxyFileLogger(string categoryName, Action<string> writeLine)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        ArgumentNullException.ThrowIfNull(writeLine);

        _categoryName = categoryName;
        _writeLine = writeLine;
    }

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NullScope.Instance;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        if (!IsEnabled(logLevel))
        {
            return;
        }

        string message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        _writeLine(FormatLogEntry(_categoryName, logLevel, eventId, message, exception));
    }

    internal static string FormatLogEntry(string categoryName, LogLevel logLevel, EventId eventId, string? message, Exception? exception)
    {
        ArgumentNullException.ThrowIfNull(categoryName);

        var builder = new StringBuilder();
        builder.Append(DateTime.UtcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture));
        builder.Append(" [");
        builder.Append(logLevel.ToString());
        builder.Append("] ");
        builder.Append(categoryName);

        if (eventId.Id != 0 || !string.IsNullOrWhiteSpace(eventId.Name))
        {
            builder.Append(" {EventId=");
            builder.Append(eventId.Id.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(eventId.Name))
            {
                builder.Append(", Name=");
                builder.Append(eventId.Name);
            }

            builder.Append('}');
        }

        if (!string.IsNullOrWhiteSpace(message))
        {
            builder.Append(": ");
            builder.Append(message.Trim());
        }

        if (exception is not null)
        {
            builder.AppendLine();
            builder.Append(exception);
        }

        return builder.ToString();
    }

    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
