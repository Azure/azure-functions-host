// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string EventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    private const int ConsoleBufferSize = 8_000;
    private const string EventTimestampFormat = "O";

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
        DateTimeOffset? timestamp = null)
    {
        _writer.WriteHandler(Format(new(
            (int)ToEventLevel(level),
            eventName,
            source,
            details,
            FormatSummary(summary, context),
            timestamp ?? _timeProvider.GetUtcNow(),
            exceptionType,
            exceptionMessage,
            invocationId)));
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
        Add(nameof(context.Operation), context.Operation);
        Add(nameof(context.Stage), context.Stage);
        Add(nameof(context.Outcome), context.Outcome);
        Add(nameof(context.ReasonCode), context.ReasonCode);
        Add(nameof(context.DurationMs), context.DurationMs?.ToString(CultureInfo.InvariantCulture));
        if (context.Attributes is { } attributes)
        {
            Add(nameof(attributes.HttpStatusCode), attributes.HttpStatusCode?.ToString(CultureInfo.InvariantCulture));
            Add(nameof(attributes.ForwarderError), attributes.ForwarderError);
            Add(nameof(attributes.RpcStatus), attributes.RpcStatus);
            Add(nameof(attributes.PeerSide), attributes.PeerSide);
            Add(nameof(attributes.StateRevision), attributes.StateRevision?.ToString(CultureInfo.InvariantCulture));
            Add(nameof(attributes.WorkerReady), attributes.WorkerReady?.ToString());
            Add(nameof(attributes.AssignmentState), attributes.AssignmentState);
        }

        return fields.Length == 0
            ? summary
            : fields.Append(summary.AsSpan(0, Math.Min(summary.Length, 1_024))).ToString();

        void Add(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                fields.Append(name).Append('=').Append(value.AsSpan(0, Math.Min(value.Length, 128))).Append("; ");
            }
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
            NormalizeIdentifier(record.InvocationId),
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

    private static string NormalizeColumn(string value, int limit)
    {
        value = value[..Math.Min(value.Length, limit)]
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
