// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.Functions.WorkerProxy.Diagnostics;

internal sealed class MsFunctionLogsLogger : ILogger
{
    private const string EventStreamName = "MS_FUNCTION_LOGS";
    private const string EventTimestampFormat = "O";
    private const int MaxDetailsLength = 10000;
    private const string EmptyQuotedField = "\"\"";
    private static readonly string ProcessId = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

    private readonly string _categoryName;
    private readonly string _containerName;
    private readonly string _stampName;
    private readonly string _tenantId;
    private readonly Action<string> _writeLine;

    internal MsFunctionLogsLogger(string categoryName, Action<string> writeLine, string containerName, string stampName, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(categoryName);
        ArgumentNullException.ThrowIfNull(writeLine);

        _categoryName = categoryName;
        _writeLine = writeLine;
        _containerName = containerName;
        _stampName = stampName;
        _tenantId = tenantId;
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

        string summary = formatter(state, exception) ?? string.Empty;
        string details = exception?.ToString() ?? string.Empty;

        if (details.Length > MaxDetailsLength)
        {
            details = details[..MaxDetailsLength];
        }

        string[] fields =
        [
            ToEventLevel(logLevel).ToString(CultureInfo.InvariantCulture),        // Level
            string.Empty,                                                         // SubscriptionId
            string.Empty,                                                         // AppName
            string.Empty,                                                         // FunctionName
            string.Empty,                                                         // EventName
            _categoryName,                                                        // Source
            NormalizeString(details),                                             // Details
            NormalizeString(summary),                                             // Summary
            string.Empty,                                                         // HostVersion
            DateTime.UtcNow.ToString(EventTimestampFormat, CultureInfo.InvariantCulture), // EventTimestamp
            string.Empty,                                                         // ExceptionType
            EmptyQuotedField,                                                     // ExceptionMessage
            string.Empty,                                                         // FunctionInvocationId
            string.Empty,                                                         // HostInstanceId
            string.Empty,                                                         // ActivityId
            _containerName,                                                       // ContainerName
            _stampName,                                                           // StampName
            _tenantId,                                                            // TenantId
            string.Empty,                                                         // RuntimeSiteName
            string.Empty,                                                         // SlotName
            ProcessId                                                             // Pid
        ];

        _writeLine($"{EventStreamName} {string.Join(",", fields)}");
    }

    internal static string NormalizeString(string? value, bool addEnclosingQuotes = true)
    {
        string normalized = value ?? string.Empty;
        normalized = normalized.Replace(Environment.NewLine, " ", StringComparison.Ordinal);
        normalized = normalized.Replace("\"", "'", StringComparison.Ordinal);

        return addEnclosingQuotes ? $"\"{normalized}\"" : normalized;
    }

    internal static int ToEventLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace or LogLevel.Debug => 5,
            LogLevel.Information => 4,
            LogLevel.Warning => 3,
            LogLevel.Error => 2,
            LogLevel.Critical => 1,
            _ => 0
        };
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
