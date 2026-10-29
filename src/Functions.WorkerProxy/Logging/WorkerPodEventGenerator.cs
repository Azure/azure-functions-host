// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Tracing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerPodEventGenerator : IWorkerSystemEventGenerator, IDisposable
{
    internal const string EventStreamName = "MS_FUNCTIONS_WORKER_POD_LOGS";
    internal const string WorkerSource = "Worker.LanguageWorker";
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
            static _ => { })
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
        string invocationId)
    {
        Write(new(
            (int)ToEventLevel(level),
            eventName,
            source,
            details,
            summary,
            _timeProvider.GetUtcNow(),
            exceptionType,
            exceptionMessage,
            invocationId));
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
            NormalizeString(record.Details),
            NormalizeString(record.Summary),
            string.Empty,
            record.Timestamp.UtcDateTime.ToString(
                EventTimestampFormat,
                CultureInfo.InvariantCulture),
            record.ExceptionType,
            NormalizeString(record.ExceptionMessage),
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

        return $"{EventStreamName} {string.Join(',', fields)}";
    }

    internal void Write(WorkerSystemLogRecord record)
    {
        _writer.WriteHandler(Format(record));
    }

    private static string NormalizeString(string value)
    {
        value = value.Replace(Environment.NewLine, " ");
        value = value.Replace("\"", "'");

        return $"\"{value}\"";
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
