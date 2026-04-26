// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Diagnostics;
using System.Globalization;

namespace Microsoft.Azure.Functions.WorkerProxy.Supervisor;

internal sealed class FunctionLogWriter
{
    private const string EventStreamName = "MS_FUNCTION_LOGS";
    private const string MetricsStreamName = "MS_FUNCTION_METRICS";
    private const string EventTimestampFormat = "O";
    private const string SupervisorSource = "workerproxy.supervisor";
    private const string ProcessEventName = "workerproxy.process";
    private const string EmptyQuotedField = "\"\"";

    private readonly TextWriter _writer;
    private readonly WorkerProxySupervisorLogContext _context;
    private readonly Func<DateTime> _utcNow;
    private readonly string _processId;

    public FunctionLogWriter(
        TextWriter writer,
        WorkerProxySupervisorLogContext context,
        Func<DateTime>? utcNow = null,
        string? processId = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _processId = processId ?? Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture);
    }

    public static FunctionLogWriter CreateFromEnvironment(TextWriter writer)
        => new(writer, WorkerProxySupervisorLogContext.FromEnvironment());

    public void WriteProcessLine(string line)
    {
        ArgumentNullException.ThrowIfNull(line);

        if (line.StartsWith(EventStreamName, StringComparison.Ordinal)
            || line.StartsWith(MetricsStreamName, StringComparison.Ordinal))
        {
            WriteLine(line);
            return;
        }

        if (line.Length > 0)
        {
            WriteSupervisorMessage(2, $"workerproxy-process: {line}");
        }
    }

    public void WriteSupervisorMessage(int level, string summary)
    {
        string[] fields =
        [
            level.ToString(CultureInfo.InvariantCulture),
            string.Empty,
            string.Empty,
            string.Empty,
            ProcessEventName,
            SupervisorSource,
            NormalizeString(string.Empty),
            NormalizeString(summary),
            _context.HostVersion,
            _utcNow().ToString(EventTimestampFormat, CultureInfo.InvariantCulture),
            string.Empty,
            EmptyQuotedField,
            string.Empty,
            string.Empty,
            string.Empty,
            _context.ContainerName,
            _context.StampName,
            _context.TenantId,
            string.Empty,
            string.Empty,
            _processId
        ];

        WriteLine($"{EventStreamName} {string.Join(',', fields)}");
    }

    private static string NormalizeString(string? value)
    {
        string normalized = value ?? string.Empty;
        normalized = normalized.Replace("\r", " ", StringComparison.Ordinal);
        normalized = normalized.Replace("\n", " ", StringComparison.Ordinal);
        normalized = normalized.Replace("\"", "'", StringComparison.Ordinal);

        return $"\"{normalized}\"";
    }

    private void WriteLine(string line)
    {
        try
        {
            _writer.WriteLine(line);
            _writer.Flush();
        }
        catch
        {
            // Logging is best-effort; it should not take down the supervisor.
        }
    }
}
