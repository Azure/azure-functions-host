// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerProxyLogExporter(IWorkerSystemEventGenerator eventGenerator)
    : BaseExporter<LogRecord>
{
    internal static readonly string ApplicationCategory =
        typeof(WorkerProxyApplication).FullName!;

    internal static readonly string AssignmentCategory =
        typeof(WorkerAssignmentLoggingMiddleware).FullName!;

    internal static readonly string ReadinessProbeCategory =
        typeof(WorkerEndpointReadinessProbe).FullName!;

    internal static readonly string RelayCategory =
        typeof(FunctionRpcRelay).FullName!;

    private readonly IWorkerSystemEventGenerator _eventGenerator =
        eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));

    public override ExportResult Export(in Batch<LogRecord> batch)
    {
        foreach (LogRecord record in batch)
        {
            if (record.LogLevel is LogLevel.None)
            {
                continue;
            }

            string? message = record.FormattedMessage ?? record.Body;
            if (string.IsNullOrEmpty(message))
            {
                continue;
            }

            string details = string.Empty;
            string exceptionType = string.Empty;
            string exceptionMessage = string.Empty;
            if (record.Exception is { } exception)
            {
                Exception baseException = exception.GetBaseException();
                exceptionType = baseException.GetType().ToString();
                exceptionMessage = baseException.Message;
                details = exception.ToString();
            }

            ScopeState scopeState = new();
            record.ForEachScope(
                static (scope, state) =>
                {
                    if (scope.Scope is WorkerSystemLogContext context)
                    {
                        state.Context = state.Context.Merge(context);
                    }
                },
                scopeState);

            _eventGenerator.LogEvent(
                record.LogLevel,
                record.EventId.Name ?? string.Empty,
                record.CategoryName ?? string.Empty,
                details,
                message,
                exceptionType,
                exceptionMessage,
                scopeState.Context,
                record.Timestamp);
        }

        return ExportResult.Success;
    }

    // The shared writer drains on generator disposal; it has no non-destructive flush operation.
    protected override bool OnForceFlush(int timeoutMilliseconds) => false;

    private sealed class ScopeState
    {
        public WorkerSystemLogContext Context { get; set; } = new();
    }
}
