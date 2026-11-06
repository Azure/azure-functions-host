// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string EventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    private const int ConsoleBufferSize = 8_000;

    private readonly BufferedConsoleWriter _writer;
    private readonly TimeProvider _timeProvider;
    private int _healthReported;
    private long _droppedRecords;

    public WorkerPodEventGenerator(TimeProvider timeProvider)
        : this(Console.Out, timeProvider)
    {
    }

    internal WorkerPodEventGenerator(TextWriter writer, TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _writer = new BufferedConsoleWriter(
            ConsoleBufferSize,
            exception => ReportBufferFailure(exception.GetType().Name))
        {
            Writer = writer ?? throw new ArgumentNullException(nameof(writer))
        };
    }

    internal long DroppedRecords => Interlocked.Read(ref _droppedRecords);

    public void LogEvent(
        LogLevel level,
        string eventName,
        string source,
        string details,
        string summary,
        string exceptionType,
        string exceptionMessage,
        WorkerSystemLogContext? context = null,
        DateTimeOffset? timestamp = null)
    {
        if (!_writer.TryWrite(Format(new(
            (int)ToEventLevel(level),
            eventName,
            source,
            details,
            FormatSummary(summary, context),
            timestamp ?? _timeProvider.GetUtcNow(),
            exceptionType,
            exceptionMessage))))
        {
            Interlocked.Increment(ref _droppedRecords);
            ReportBufferFailure("BufferUnavailable");
        }
    }

    private void ReportBufferFailure(string reason)
    {
        if (Interlocked.Exchange(ref _healthReported, 1) != 0)
        {
            return;
        }

        // Never block a forwarding thread or send a health record back through the failed buffer.
        ThreadPool.QueueUserWorkItem(static state =>
        {
            try
            {
                Console.Error.WriteLine($"WorkerProxy system log loss detected ({state}); further notices suppressed.");
            }
            catch (IOException)
            {
                // Both console sinks may be unavailable during teardown. There is no third sink to retry.
            }
            catch (ObjectDisposedException)
            {
                // The process console has already been closed.
            }
        }, reason);
    }

    private static string FormatSummary(string summary, WorkerSystemLogContext? context)
    {
        if (context is null)
        {
            return summary;
        }

        StringBuilder fields = new();
        Add(nameof(context.WorkerId), NormalizeIdentifier(context.WorkerId));
        Add(nameof(context.ProxySessionId), context.ProxySessionId?.ToString(CultureInfo.InvariantCulture));
        StringBuilder metadata = new();
        bool omitted = false;
        AddMetadata("p", context.State?.PodName);
        AddMetadata("a", context.State?.FunctionAppName);
        AddMetadata("g", context.State?.FunctionGroupName);
        AddMetadata("r", context.EventRevision ?? context.State?.Revision);
        AddMetadata("sr", context.EventRevision is not null ? context.State?.Revision : null);
        AddMetadata("s", context.State?.AssignmentState);
        AddMetadata("wr", context.State?.IsWorkerReady);
        AddMetadata("rm", context.State?.RemovalRequested);
        AddMetadata("q", context.RequestId);
        AddMetadata("i", context.InvocationId);
        AddMetadata("f", context.FunctionId);
        AddMetadata("op", context.Operation);
        AddMetadata("ph", context.Phase);
        AddMetadata("o", context.Outcome);
        AddMetadata("rc", context.ReasonCode);
        AddMetadata("sd", context.Side);
        AddMetadata("sc", context.StatusCode);
        AddMetadata("rpc", context.RpcStatus);
        AddMetadata("ye", context.ForwarderError);
        AddMetadata("ms", context.ElapsedMs);
        AddMetadata("cs", context.CancellationSource);
        AddMetadata("at", context.Attempts);
        AddMetadata("rs", context.ResponseStarted);
        AddMetadata("n", context.FunctionCount);
        AddMetadata("uf", context.UnsuccessfulCount);
        AddMetadata("pm", context.PendingMessages);
        AddMetadata("b", context.BuildVersion);
        if (metadata.Length > 0 || omitted)
        {
            fields.Append("WP1[").Append(metadata);
            if (omitted)
            {
                fields.Append("omit=1;");
            }

            fields.Append("]; ");
        }

        return fields.Length == 0
            ? summary
            : fields.Append(summary.AsSpan(0, GetTruncatedLength(summary, 1_024))).ToString();

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                fields.Append(name).Append('=').Append(value.AsSpan(0, GetTruncatedLength(value, 128))).Append("; ");
            }
        }

        void AddMetadata(string key, object? value)
        {
            string? text = value switch
            {
                null => null,
                bool flag => flag ? "true" : "false",
                double number => number.ToString("F3", CultureInfo.InvariantCulture),
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString()
            };
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            text = text.Length <= 128
                && text.AsSpan().IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.:+") < 0
                    ? text : NormalizeIdentifier(text);
            // Reserve the complete envelope, omission marker, and at least 128 characters of readable Summary.
            if (fields.Length + metadata.Length + key.Length + text.Length + 16 > 896)
            {
                omitted = true;
                return;
            }

            metadata.Append(key).Append('=').Append(text).Append(';');
        }
    }

    private static string NormalizeIdentifier(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (value.Length <= 128
            && value.AsSpan().IndexOfAnyExcept("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.") < 0)
        {
            return value;
        }

        // Do not truncate identities into collisions or copy arbitrary peer-supplied text into columns.
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";
    }

    internal string Format(WorkerSystemLogRecord record)
    {
        // Preserve the Host trace layout, leaving Host-only identity columns empty.
        return string.Create(CultureInfo.InvariantCulture,
            $"{EventStreamName} {record.Level},,,,{NormalizeColumn(record.EventName, 128)},{NormalizeColumn(record.Source, 256)},{NormalizeString(record.Details, 1_024)},{NormalizeString(record.Summary, 1_024)},,{record.Timestamp.UtcDateTime:O},{NormalizeColumn(record.ExceptionType, 256)},{NormalizeString(record.ExceptionMessage, 1_024)},,,,,,,,,");
    }

    private static string NormalizeColumn(string value, int limit)
    {
        value = value[..GetTruncatedLength(value, limit)]
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

    private static int GetTruncatedLength(string value, int limit)
    {
        int length = Math.Min(value.Length, limit);
        if (length > 0 && length < value.Length
            && char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
        {
            length--;
        }

        return length;
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
        string ExceptionMessage);
}
