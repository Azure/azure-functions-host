// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string WorkerEventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    internal const string ProxyEventStreamName = "MS_FUNCTIONS_WORKER_PROXY_LOGS";
    internal const string WorkerSource = "Worker.LanguageWorker";
    internal const string ProxySource = "Worker.Proxy";
    internal const int MaximumRecordBytes = 32 * 1024;
    internal const int MaximumMessageBytes = 4_000;
    internal const int MaximumRemoteExceptionMessageBytes = 2_000;
    internal const int MaximumRemoteStackTraceBytes = 6_000;
    internal const int QueueCapacity = 256;
    private const int MaximumExceptionFieldBytes = 12_000;
    private const int MaximumEventNameBytes = 256;
    private const int MaximumSanitizationInputBytes = 64 * 1024;
    private const string OversizedFieldReplacement = "[Oversized worker log field omitted]";
    private const string TruncationSuffix = "...[truncated]";
    private const string RpcExceptionType = "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException";

    private readonly BufferedConsoleWriter _writer;
    private readonly TimeProvider _timeProvider;

    public WorkerPodEventGenerator(TimeProvider timeProvider)
        : this(Console.Out, timeProvider)
    {
    }

    internal WorkerPodEventGenerator(TextWriter writer, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _writer = new BufferedConsoleWriter(
            QueueCapacity,
            static _ => { },
            consoleBufferTimeout: TimeSpan.Zero,
            autoStart: true,
            BufferedConsoleWriterFullMode.DropWrite,
            CreateDroppedSummary)
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer))
        };
    }

    internal long TotalDroppedRecords => _writer.TotalDroppedWrites;

    public void LogWorkerEvent(RpcLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        TryEnqueue(CreateWorkerRecord(log, _timeProvider.GetUtcNow()));
    }

    public void LogProxyEvent(LogLevel level, string eventName, string message)
    {
        TryEnqueue(CreateProxyRecord(level, eventName, message, _timeProvider.GetUtcNow()));
    }

    internal WorkerSystemLogRecord CreateWorkerRecord(RpcLog log, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(log);

        LogLevel level = MapLogLevel(log.Level);
        string summary = SanitizeAndTruncate(log.Message, MaximumMessageBytes);
        if (level is not (LogLevel.Error or LogLevel.Critical) || log.Exception is null)
        {
            return new(
                MapEventLevel(level),
                string.Empty,
                WorkerSource,
                string.Empty,
                summary,
                timestamp,
                string.Empty,
                string.Empty);
        }

        string remoteMessage = SanitizeAndTruncate(
            log.Exception.Message,
            MaximumRemoteExceptionMessageBytes);
        string remoteStack = SanitizeAndTruncate(
            log.Exception.StackTrace,
            MaximumRemoteStackTraceBytes);
        string exceptionMessage = TruncateUtf8(
            $"Result: {summary}\nType: \nException: {remoteMessage}\nStack: {remoteStack}",
            MaximumExceptionFieldBytes);
        string details = TruncateUtf8(
            $"{RpcExceptionType}: {exceptionMessage}",
            MaximumExceptionFieldBytes);

        return new(
            MapEventLevel(level),
            string.Empty,
            WorkerSource,
            details,
            summary,
            timestamp,
            RpcExceptionType,
            exceptionMessage);
    }

    internal WorkerSystemLogRecord CreateProxyRecord(
        LogLevel level,
        string eventName,
        string message,
        DateTimeOffset timestamp)
    {
        return new(
            MapEventLevel(level),
            TruncateUtf8(eventName, MaximumEventNameBytes),
            ProxySource,
            string.Empty,
            TruncateUtf8(message, MaximumMessageBytes),
            timestamp,
            string.Empty,
            string.Empty);
    }

    internal string Format(WorkerSystemLogRecord record)
    {
        string[] fields =
        [
            record.Level.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty,
            SanitizeUnquoted(record.EventName),
            record.Source,
            Quote(record.Details),
            Quote(record.Summary),
            string.Empty,
            record.Timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            SanitizeUnquoted(record.ExceptionType),
            Quote(record.ExceptionMessage),
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        ];

        string formatted = $"{GetEventStreamName(record.Source)} {string.Join(',', fields)}";
        return Encoding.UTF8.GetByteCount(formatted) <= MaximumRecordBytes
            ? formatted
            : CreateOversizedRecord(record.Level, record.Source, record.Timestamp);
    }

    internal bool TryEnqueue(WorkerSystemLogRecord record)
    {
        return _writer.TryWrite(Format(record));
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    internal Task CompleteAsync()
    {
        return _writer.CompleteAsync();
    }

    private static string CreateOversizedRecord(int level, string source, DateTimeOffset timestamp)
    {
        string[] fields =
        [
            level.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty,
            "WorkerSystemLogOmitted",
            source,
            "\"\"",
            "\"Worker system log exceeded the maximum encoded record size.\"",
            string.Empty,
            timestamp.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
            string.Empty,
            "\"\"",
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        ];

        return $"{GetEventStreamName(source)} {string.Join(',', fields)}";
    }

    private static int MapEventLevel(LogLevel level)
    {
        EventLevel eventLevel = level switch
        {
            LogLevel.Critical => EventLevel.Critical,
            LogLevel.Error => EventLevel.Error,
            LogLevel.Warning => EventLevel.Warning,
            LogLevel.Information => EventLevel.Informational,
            _ => EventLevel.Verbose
        };

        return (int)eventLevel;
    }

    private static string GetEventStreamName(string source)
    {
        return string.Equals(source, ProxySource, StringComparison.Ordinal)
            ? ProxyEventStreamName
            : WorkerEventStreamName;
    }

    private static LogLevel MapLogLevel(RpcLog.Types.Level level)
    {
        return level switch
        {
            RpcLog.Types.Level.Warning => LogLevel.Warning,
            RpcLog.Types.Level.Error => LogLevel.Error,
            RpcLog.Types.Level.Critical => LogLevel.Critical,
            _ => LogLevel.Information
        };
    }

    private static string SanitizeAndTruncate(string value, int maximumBytes)
    {
        if (value.Length > MaximumSanitizationInputBytes
            || Encoding.UTF8.GetByteCount(value) > MaximumSanitizationInputBytes)
        {
            return OversizedFieldReplacement;
        }

        return TruncateUtf8(Sanitizer.Sanitize(value), maximumBytes);
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
        {
            return value;
        }

        int contentBudget = maximumBytes - Encoding.UTF8.GetByteCount(TruncationSuffix);
        int bytesUsed = 0;
        int charsUsed = 0;
        ReadOnlySpan<char> remaining = value.AsSpan();

        while (!remaining.IsEmpty)
        {
            OperationStatus status = Rune.DecodeFromUtf16(remaining, out Rune rune, out int charsConsumed);
            if (status != OperationStatus.Done || bytesUsed + rune.Utf8SequenceLength > contentBudget)
            {
                break;
            }

            bytesUsed += rune.Utf8SequenceLength;
            charsUsed += charsConsumed;
            remaining = remaining[charsConsumed..];
        }

        return string.Concat(value.AsSpan(0, charsUsed), TruncationSuffix);
    }

    private static string Quote(string value) => $"\"{SanitizeQuoted(value)}\"";

    private static string SanitizeQuoted(string value)
    {
        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('"', '\'');
    }

    private static string SanitizeUnquoted(string value) =>
        SanitizeQuoted(value).Replace(',', ' ');

    private string CreateDroppedSummary(long dropped)
    {
        WorkerSystemLogRecord summary = CreateProxyRecord(
            LogLevel.Warning,
            "WorkerSystemLogsDropped",
            $"WorkerProxy dropped {dropped} system log records because the console queue was unavailable.",
            _timeProvider.GetUtcNow());

        return Format(summary);
    }

    internal readonly record struct WorkerSystemLogRecord(
        int Level,
        string EventName,
        string Source,
        string Details,
        string Summary,
        DateTimeOffset Timestamp,
        string ExceptionType,
        string ExceptionMessage);
}
