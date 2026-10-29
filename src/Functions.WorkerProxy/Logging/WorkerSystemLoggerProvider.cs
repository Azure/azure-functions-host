// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerSystemLoggerProvider(IWorkerSystemEventGenerator eventGenerator) : ILoggerProvider
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

    public ILogger CreateLogger(string categoryName)
    {
        return new SystemLogger(_eventGenerator);
    }

    public void Dispose()
    {
    }

    private sealed class SystemLogger(IWorkerSystemEventGenerator eventGenerator) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull =>
            null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            string message = formatter(state, exception);
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            eventGenerator.LogProxyEvent(
                logLevel,
                eventId.Name ?? string.Empty,
                message);
        }
    }
}
