// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
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
    internal const string InvocationIdScopeKey = "InvocationId";

    internal static readonly string ApplicationCategory =
        typeof(WorkerProxyApplication).FullName!;

    internal static readonly string AssignmentCategory =
        typeof(WorkerAssignmentLoggingMiddleware).FullName!;

    internal static readonly string ReadinessProbeCategory =
        typeof(WorkerEndpointReadinessProbe).FullName!;

    internal static readonly string RelayCategory =
        typeof(FunctionRpcRelay).FullName!;

    internal static readonly string HttpCategory =
        typeof(WorkerHttpForwardingMiddleware).FullName!;

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

            InvocationState invocationState = new();
            record.ForEachScope(
                static (scope, state) =>
                {
                    if (scope.Scope is WorkerSystemLogContext context)
                    {
                        state.Context = state.Context.Merge(context);
                        return;
                    }

                    if (scope.Scope is not IEnumerable<KeyValuePair<string, object?>> properties)
                    {
                        return;
                    }

                    foreach (KeyValuePair<string, object?> property in properties)
                    {
                        if (string.Equals(
                            property.Key,
                            InvocationIdScopeKey,
                            StringComparison.Ordinal)
                            && property.Value is not null)
                        {
                            state.Context = state.Context with { InvocationId = property.Value.ToString() };
                        }
                    }
                },
                invocationState);

            _eventGenerator.LogEvent(
                record.LogLevel,
                record.EventId.Name ?? string.Empty,
                record.CategoryName ?? string.Empty,
                details,
                message,
                exceptionType,
                exceptionMessage,
                invocationState.Context.InvocationId ?? string.Empty,
                invocationState.Context,
                record.Timestamp,
                record.TraceId,
                record.SpanId);
        }

        return ExportResult.Success;
    }

    // The shared writer drains on generator disposal; it has no non-destructive flush operation.
    protected override bool OnForceFlush(int timeoutMilliseconds) => false;

    private sealed class InvocationState
    {
        public WorkerSystemLogContext Context { get; set; } = new();
    }
}
