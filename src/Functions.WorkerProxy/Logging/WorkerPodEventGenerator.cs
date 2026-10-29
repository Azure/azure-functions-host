// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string EventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    internal const string WorkerSource = "Worker.LanguageWorker";
    internal const string ProxySource = "Worker.Proxy";
    internal const int MaximumRecordBytes = 32 * 1024;
    internal const int MaximumMessageBytes = 4_000;
    internal const int MaximumRemoteExceptionMessageBytes = 2_000;
    internal const int MaximumRemoteStackTraceBytes = 6_000;
    internal const int QueueCapacity = 256;
    private const int MaximumExceptionFieldBytes = 12_000;
    private const int MaximumEventNameBytes = 256;
    private const int MaximumCredentialTokenMatches = 64;
    private const int MaximumSanitizationInputBytes = 64 * 1024;
    private const string SanitizationWorkLimitReplacement = "[Worker log field omitted because sanitization work exceeded the limit]";
    private const string OversizedFieldReplacement = "[Oversized worker log field omitted]";
    private const string TruncationSuffix = "...[truncated]";
    private const string RpcExceptionType = "Microsoft.Azure.WebJobs.Script.Workers.Rpc.RpcException";

    private readonly BufferedConsoleWriter _writer;
    private readonly Channel<PendingWorkerLog> _workerLogQueue;
    private readonly Task _workerLogProcessingTask;
    private readonly TimeProvider _timeProvider;
    private long _pendingDroppedWorkerLogs;
    private long _totalDroppedWorkerLogs;

    public WorkerPodEventGenerator(TimeProvider timeProvider)
        : this(Console.Out, timeProvider)
    {
    }

    internal WorkerPodEventGenerator(TextWriter writer, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _workerLogQueue = Channel.CreateBounded<PendingWorkerLog>(
            new BoundedChannelOptions(QueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false
            });
        _writer = new BufferedConsoleWriter(
            QueueCapacity,
            static _ => { })
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer))
        };
        _workerLogProcessingTask = ProcessWorkerLogsAsync();
    }

    internal long TotalDroppedRecords => Interlocked.Read(ref _totalDroppedWorkerLogs);

    public void LogWorkerEvent(RpcLog log)
    {
        ArgumentNullException.ThrowIfNull(log);

        PendingWorkerLog pendingLog = new(CreateWorkerLogSnapshot(log), _timeProvider.GetUtcNow());
        if (!_workerLogQueue.Writer.TryWrite(pendingLog))
        {
            RecordWorkerLogDrop();
        }
    }

    public void LogProxyEvent(LogLevel level, string eventName, string message)
    {
        Write(CreateProxyRecord(level, eventName, message, _timeProvider.GetUtcNow()));
    }

    internal WorkerSystemLogRecord CreateWorkerRecord(RpcLog log, DateTimeOffset timestamp)
    {
        ArgumentNullException.ThrowIfNull(log);

        LogLevel level = MapLogLevel(log.Level);
        string summary = SanitizeWorkerField(log.Message, MaximumMessageBytes);
        if (level is not (LogLevel.Error or LogLevel.Critical) || log.Exception is null)
        {
            return new(
                (int)LinuxEventFormatting.ToEventLevel(level),
                log.EventId,
                WorkerSource,
                string.Empty,
                summary,
                timestamp,
                string.Empty,
                string.Empty,
                log.InvocationId);
        }

        string remoteMessage = SanitizeWorkerField(
            log.Exception.Message,
            MaximumRemoteExceptionMessageBytes);
        string remoteStack = SanitizeWorkerField(
            log.Exception.StackTrace,
            MaximumRemoteStackTraceBytes);
        string exceptionMessage = TruncateUtf8(
            $"Result: {summary}\nType: \nException: {remoteMessage}\nStack: {remoteStack}",
            MaximumExceptionFieldBytes);
        string details = TruncateUtf8(
            $"{RpcExceptionType}: {exceptionMessage}",
            MaximumExceptionFieldBytes);

        return new(
            (int)LinuxEventFormatting.ToEventLevel(level),
            log.EventId,
            WorkerSource,
            details,
            summary,
            timestamp,
            RpcExceptionType,
            exceptionMessage,
            log.InvocationId);
    }

    internal WorkerSystemLogRecord CreateProxyRecord(
        LogLevel level,
        string eventName,
        string message,
        DateTimeOffset timestamp)
    {
        return new(
            (int)LinuxEventFormatting.ToEventLevel(level),
            TruncateUtf8(eventName, MaximumEventNameBytes),
            ProxySource,
            string.Empty,
            TruncateUtf8(message, MaximumMessageBytes),
            timestamp,
            string.Empty,
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
            record.EventName,
            record.Source,
            LinuxEventFormatting.NormalizeString(record.Details),
            LinuxEventFormatting.NormalizeString(record.Summary),
            string.Empty,
            record.Timestamp.UtcDateTime.ToString(
                LinuxEventFormatting.EventTimestampFormat,
                CultureInfo.InvariantCulture),
            record.ExceptionType,
            LinuxEventFormatting.NormalizeString(record.ExceptionMessage),
            record.InvocationId,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty
        ];

        string formatted = $"{EventStreamName} {string.Join(',', fields)}";
        return Encoding.UTF8.GetByteCount(formatted) <= MaximumRecordBytes
            ? formatted
            : CreateOversizedRecord(record.Level, record.Source, record.Timestamp);
    }

    internal void Write(WorkerSystemLogRecord record)
    {
        _writer.WriteHandler(Format(record));
    }

    public void Dispose()
    {
        _workerLogQueue.Writer.TryComplete();
        _workerLogProcessingTask.GetAwaiter().GetResult();
        _writer.Dispose();
    }

    internal async Task CompleteAsync()
    {
        _workerLogQueue.Writer.TryComplete();
        await _workerLogProcessingTask;
        await _writer.CompleteAsync();
    }

    private static LogLevel MapLogLevel(RpcLog.Types.Level level)
    {
        return level switch
        {
            RpcLog.Types.Level.Trace => LogLevel.Trace,
            RpcLog.Types.Level.Debug => LogLevel.Debug,
            RpcLog.Types.Level.Information => LogLevel.Information,
            RpcLog.Types.Level.Warning => LogLevel.Warning,
            RpcLog.Types.Level.Error => LogLevel.Error,
            RpcLog.Types.Level.Critical => LogLevel.Critical,
            _ => LogLevel.None
        };
    }

    private static string CreateOversizedRecord(
        int level,
        string source,
        DateTimeOffset timestamp)
    {
        string[] fields =
        [
            level.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty,
            "WorkerSystemLogOmitted",
            source,
            LinuxEventFormatting.NormalizeString(string.Empty),
            LinuxEventFormatting.NormalizeString(
                "Worker system log exceeded the maximum encoded record size."),
            string.Empty,
            timestamp.UtcDateTime.ToString(
                LinuxEventFormatting.EventTimestampFormat,
                CultureInfo.InvariantCulture),
            string.Empty,
            LinuxEventFormatting.NormalizeString(string.Empty),
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

        return $"{EventStreamName} {string.Join(',', fields)}";
    }

    private static string SanitizeWorkerField(string value, int maximumBytes)
    {
        if (value.Length > MaximumSanitizationInputBytes
            || Encoding.UTF8.GetByteCount(value) > MaximumSanitizationInputBytes)
        {
            return OversizedFieldReplacement;
        }

        if (ExceedsSanitizationWorkLimit(value))
        {
            return SanitizationWorkLimitReplacement;
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
            OperationStatus status = Rune.DecodeFromUtf16(
                remaining,
                out Rune rune,
                out int charsConsumed);
            if (status != OperationStatus.Done
                || bytesUsed + rune.Utf8SequenceLength > contentBudget)
            {
                break;
            }

            bytesUsed += rune.Utf8SequenceLength;
            charsUsed += charsConsumed;
            remaining = remaining[charsConsumed..];
        }

        return string.Concat(value.AsSpan(0, charsUsed), TruncationSuffix);
    }

    private static bool ExceedsSanitizationWorkLimit(string value)
    {
        int matches = 0;
        foreach (string token in Sanitizer.CredentialTokens)
        {
            int startIndex = 0;
            while ((startIndex = value.IndexOf(token, startIndex, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                matches++;
                if (matches > MaximumCredentialTokenMatches)
                {
                    return true;
                }

                startIndex += token.Length;
            }
        }

        return false;
    }

    private static RpcLog CreateWorkerLogSnapshot(RpcLog log)
    {
        RpcLog snapshot = new()
        {
            Level = log.Level,
            Message = log.Message
        };

        if (log.Exception is { } exception)
        {
            snapshot.Exception = new RpcException
            {
                Message = exception.Message,
                StackTrace = exception.StackTrace
            };
        }

        snapshot.EventId = log.EventId;
        snapshot.InvocationId = log.InvocationId;

        return snapshot;
    }

    private async Task ProcessWorkerLogsAsync()
    {
        await foreach (PendingWorkerLog pendingLog in _workerLogQueue.Reader.ReadAllAsync())
        {
            WriteDroppedWorkerLogSummary();
            Write(CreateWorkerRecord(pendingLog.Log, pendingLog.Timestamp));
        }

        WriteDroppedWorkerLogSummary();
    }

    private void WriteDroppedWorkerLogSummary()
    {
        long dropped = Interlocked.Exchange(ref _pendingDroppedWorkerLogs, 0);
        if (dropped <= 0)
        {
            return;
        }

        Write(
            CreateProxyRecord(
                LogLevel.Warning,
                "WorkerSystemLogsDropped",
                $"WorkerProxy dropped {dropped} worker system log records because the worker log queue was unavailable.",
                _timeProvider.GetUtcNow()));
    }

    private void RecordWorkerLogDrop()
    {
        Interlocked.Increment(ref _pendingDroppedWorkerLogs);
        Interlocked.Increment(ref _totalDroppedWorkerLogs);
    }

    private readonly record struct PendingWorkerLog(RpcLog Log, DateTimeOffset Timestamp);

    internal readonly record struct WorkerSystemLogRecord(
        int Level,
        string EventName,
        string Source,
        string Details,
        string Summary,
        DateTimeOffset Timestamp,
        string ExceptionType,
        string ExceptionMessage,
        string InvocationId);
}
