// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string EventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    internal const string StructuredEventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS_V2";
    private const int ConsoleBufferSize = 8_000;
    private const string EventTimestampFormat = "O";

    private readonly BufferedConsoleWriter _writer;
    private readonly TimeProvider _timeProvider;
    private readonly bool _useStructuredSystemLogs;
    private readonly string _instanceId = Guid.NewGuid().ToString("N");
    private long _sequence;

    public WorkerPodEventGenerator(TimeProvider timeProvider, IOptions<WorkerProxyOptions> options)
        : this(Console.Out, timeProvider, options.Value.UseStructuredSystemLogs)
    {
    }

    internal WorkerPodEventGenerator(TextWriter writer, TimeProvider timeProvider, bool useStructuredSystemLogs = false)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _useStructuredSystemLogs = useStructuredSystemLogs;
        _writer = new BufferedConsoleWriter(
            ConsoleBufferSize,
            static exception => Console.Error.WriteLine(
                $"WorkerProxy system log buffer failed ({exception.GetType().FullName})."))
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer))
        };
    }

    public void LogEvent(
        LogLevel level,
        string eventName,
        string source,
        string details,
        string summary,
        string exceptionType,
        string exceptionMessage,
        string invocationId,
        WorkerSystemLogContext? context = null,
        DateTimeOffset? timestamp = null,
        ActivityTraceId traceId = default,
        ActivitySpanId spanId = default)
    {
        if (_useStructuredSystemLogs)
        {
            WriteStructured(level, eventName, source, summary, exceptionType,
                (context ?? new()) with { InvocationId = invocationId },
                timestamp ?? _timeProvider.GetUtcNow(), traceId, spanId);
            return;
        }

        Write(new(
            (int)ToEventLevel(level),
            eventName,
            source,
            details,
            summary,
            timestamp ?? _timeProvider.GetUtcNow(),
            exceptionType,
            exceptionMessage,
            invocationId));
    }

    private void WriteStructured(LogLevel level, string eventName, string source, string summary,
        string exceptionType, WorkerSystemLogContext context, DateTimeOffset timestamp,
        ActivityTraceId traceId, ActivitySpanId spanId)
    {
        bool truncated = false;
        string? workerId = NormalizeIdentifier(context.WorkerId, out string? workerIdHash);
        string? invocationId = NormalizeIdentifier(context.InvocationId, out string? invocationIdHash);
        bool validLinkId = Guid.TryParseExact(context.LinkId, "N", out Guid linkId);
        WorkerSystemLogAttributes? attributes = context.Attributes is { } values
            ? values with
            {
                ForwarderError = Bound(values.ForwarderError, 128, ref truncated),
                RpcStatus = Bound(values.RpcStatus, 128, ref truncated),
                PeerSide = Bound(values.PeerSide, 128, ref truncated),
                AssignmentState = Bound(values.AssignmentState, 128, ref truncated)
            }
            : null;
        WorkerSystemLogEvent record = new()
        {
            EventTimestamp = timestamp,
            Severity = level.ToString(),
            EventName = Bound(eventName, 128, ref truncated)!,
            Category = Bound(source, 256, ref truncated)!,
            EmitterInstanceId = _instanceId,
            Sequence = Interlocked.Increment(ref _sequence),
            WorkerId = workerId,
            WorkerIdHash = workerIdHash,
            LinkId = validLinkId ? linkId.ToString("N") : null,
            InvalidLinkId = context.LinkId is not null && !validLinkId,
            ProxySessionId = context.ProxySessionId,
            InvocationId = invocationId,
            InvocationIdHash = invocationIdHash,
            TraceId = traceId != default ? traceId.ToString() : null,
            SpanId = spanId != default ? spanId.ToString() : null,
            Operation = Bound(context.Operation, 128, ref truncated),
            Stage = Bound(context.Stage, 128, ref truncated),
            Outcome = Bound(context.Outcome, 128, ref truncated),
            ReasonCode = Bound(context.ReasonCode, 128, ref truncated),
            DurationMs = context.DurationMs,
            Attributes = attributes,
            Message = Bound(summary, 1_024, ref truncated)!,
            ExceptionType = string.IsNullOrEmpty(exceptionType) ? null : Bound(exceptionType, 256, ref truncated),
            Truncated = truncated
        };
        _writer.WriteHandler($"{StructuredEventStreamName} {JsonSerializer.Serialize(record, WorkerSystemLogJsonContext.Default.WorkerSystemLogEvent)}");
    }

    private static string? Bound(string? value, int limit, ref bool truncated)
    {
        if (value is null)
        {
            return null;
        }

        value = Sanitizer.Sanitize(value);
        if (value.Length <= limit)
        {
            return value;
        }

        truncated = true;
        return value[..limit];
    }

    private static string? NormalizeIdentifier(string? value, out string? hash)
    {
        hash = null;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (value.Length <= 128
            && value.AsSpan().IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.") < 0)
        {
            return value;
        }

        // Do not truncate identities into collisions or copy arbitrary peer-supplied text into columns.
        hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return null;
    }

    internal string Format(WorkerSystemLogRecord record)
    {
        string? invocationId = NormalizeIdentifier(record.InvocationId, out string? invocationIdHash);
        string[] fields =
        [
            record.Level.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty,
            NormalizeColumn(record.EventName, 128),
            NormalizeColumn(record.Source, 256),
            NormalizeString(record.Details, 1_024),
            NormalizeString(record.Summary, 1_024),
            string.Empty,
            record.Timestamp.UtcDateTime.ToString(
                EventTimestampFormat,
                CultureInfo.InvariantCulture),
            NormalizeColumn(record.ExceptionType, 256),
            NormalizeString(record.ExceptionMessage, 1_024),
            invocationId ?? (invocationIdHash is null ? string.Empty : $"sha256:{invocationIdHash}"),
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

    internal void Write(WorkerSystemLogRecord record)
    {
        _writer.WriteHandler(Format(record));
    }

    private static string NormalizeColumn(string value, int limit)
    {
        bool truncated = false;
        value = Bound(value, limit, ref truncated)!
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('"', '\'');

        return value.Contains(',') ? $"\"{value}\"" : value;
    }

    private static string NormalizeString(string value, int limit)
    {
        string normalized = NormalizeColumn(value, limit);
        return normalized.StartsWith('"') ? normalized : $"\"{normalized}\"";
    }

    private static EventLevel ToEventLevel(LogLevel level)
    {
        return level switch
        {
            LogLevel.Trace or LogLevel.Debug => EventLevel.Verbose,
            LogLevel.Information => EventLevel.Informational,
            LogLevel.Warning => EventLevel.Warning,
            LogLevel.Error => EventLevel.Error,
            LogLevel.Critical => EventLevel.Critical,
            _ => EventLevel.LogAlways
        };
    }

    public void Dispose()
    {
        _writer.Dispose();
    }

    public bool ForceFlush(int timeoutMilliseconds)
    {
        using CancellationTokenSource timeout = new(timeoutMilliseconds);
        try
        {
            _writer.FlushAsync(timeout.Token).GetAwaiter().GetResult();
            return true;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            return false;
        }
    }

    internal async Task CompleteAsync()
    {
        await _writer.CompleteAsync();
    }

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
