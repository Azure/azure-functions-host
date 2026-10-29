// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Management;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerSystemLoggerProvider(IWorkerSystemEventGenerator eventGenerator)
    : ILoggerProvider, ISupportExternalScope
{
    internal const string InvocationIdScopeKey = "InvocationId";
    internal const string WorkerCategory = WorkerPodEventGenerator.WorkerSource;

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

    private IExternalScopeProvider _scopeProvider = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName)
    {
        return new SystemLogger(this, categoryName);
    }

    public void SetScopeProvider(IExternalScopeProvider scopeProvider)
    {
        _scopeProvider = scopeProvider ?? throw new ArgumentNullException(nameof(scopeProvider));
    }

    public void Dispose()
    {
    }

    private sealed class SystemLogger(
        WorkerSystemLoggerProvider provider,
        string categoryName) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull
        {
            return provider._scopeProvider.Push(state);
        }

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

            string details = string.Empty;
            string exceptionType = string.Empty;
            string exceptionMessage = string.Empty;
            if (exception is not null)
            {
                Exception baseException = exception.GetBaseException();
                exceptionType = baseException.GetType().ToString();
                exceptionMessage = Sanitizer.Sanitize(baseException.Message);
                details = Sanitizer.Sanitize(exception.ToString());
                message = Sanitizer.Sanitize(message);
            }

            InvocationState invocationState = new();
            provider._scopeProvider.ForEachScope(
                static (scope, state) =>
                {
                    if (scope is not IEnumerable<KeyValuePair<string, object?>> properties)
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
                            state.InvocationId = property.Value.ToString() ?? string.Empty;
                        }
                    }
                },
                invocationState);

            provider._eventGenerator.LogEvent(
                logLevel,
                eventId.Name ?? string.Empty,
                categoryName,
                details,
                message,
                exceptionType,
                exceptionMessage,
                invocationState.InvocationId);
        }
    }

    private sealed class InvocationState
    {
        public string InvocationId { get; set; } = string.Empty;
    }
}
