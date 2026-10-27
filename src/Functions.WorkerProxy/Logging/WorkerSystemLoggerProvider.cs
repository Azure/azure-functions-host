// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net.Sockets;
using Azure.Functions.WorkerProxy.Http;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Azure.Functions.WorkerProxy.Logging;

internal sealed class WorkerSystemLoggerProvider(IWorkerSystemEventGenerator eventGenerator) : ILoggerProvider
{
    private const int DestinationResolutionFailedEventId = 0;
    private const int DestinationReadyEventId = 1;
    private const int DestinationNotReadyEventId = 2;
    private const int RelaySessionFaultedEventId = 0;
    private const int RelaySessionTerminatedEventId = 1;
    private const int FunctionGroupNotAdvertisedEventId = 3;
    private const int RelayAttachmentAcceptedEventId = 4;
    private const int RelayAttachmentRejectedEventId = 5;
    private const int WorkerStartStreamAcceptedEventId = 6;
    private const int WorkerStartStreamRejectedEventId = 7;
    private const int WorkerInitializationCompletedEventId = 8;
    private const int RequestFinishedEventId = 2;
    internal const string AspNetCoreHostingCategory = "Microsoft.AspNetCore.Hosting.Diagnostics";
    internal const string AspNetCoreCategoryPrefix = "Microsoft.AspNetCore";
    internal const string GrpcCategoryPrefix = "Grpc";
    internal const string YarpCategoryPrefix = "Yarp.ReverseProxy";
    private const string WorkerAssignmentPath = "/admin/worker/assignment";

    internal static readonly string ReadinessProbeCategory =
        typeof(WorkerEndpointReadinessProbe).FullName!;

    internal static readonly string RelayCategory =
        typeof(FunctionRpcRelay).FullName!;

    private readonly IWorkerSystemEventGenerator _eventGenerator =
        eventGenerator ?? throw new ArgumentNullException(nameof(eventGenerator));

    private enum LoggerKind
    {
        AspNetCoreHosting,
        Grpc,
        Http,
        Readiness,
        Relay
    }

    public ILogger CreateLogger(string categoryName)
    {
        if (string.Equals(categoryName, AspNetCoreHostingCategory, StringComparison.Ordinal))
        {
            return new SystemLogger(_eventGenerator, LoggerKind.AspNetCoreHosting);
        }

        if (string.Equals(categoryName, ReadinessProbeCategory, StringComparison.Ordinal))
        {
            return new SystemLogger(_eventGenerator, LoggerKind.Readiness);
        }

        if (string.Equals(categoryName, RelayCategory, StringComparison.Ordinal))
        {
            return new SystemLogger(_eventGenerator, LoggerKind.Relay);
        }

        if (IsCategory(categoryName, GrpcCategoryPrefix))
        {
            return new SystemLogger(_eventGenerator, LoggerKind.Grpc, categoryName);
        }

        if (IsCategory(categoryName, AspNetCoreCategoryPrefix)
            || IsCategory(categoryName, YarpCategoryPrefix))
        {
            return new SystemLogger(_eventGenerator, LoggerKind.Http, categoryName);
        }

        return NullLogger.Instance;
    }

    public void Dispose()
    {
    }

    private static bool IsCategory(string categoryName, string categoryPrefix)
    {
        return string.Equals(categoryName, categoryPrefix, StringComparison.Ordinal)
            || categoryName.StartsWith($"{categoryPrefix}.", StringComparison.Ordinal);
    }

    private sealed class SystemLogger(
        IWorkerSystemEventGenerator eventGenerator,
        LoggerKind kind,
        string? categoryName = null) : ILogger
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
            switch (kind)
            {
                case LoggerKind.AspNetCoreHosting:
                    ConsumeAspNetCoreHostingLog(eventId, state);
                    break;
                case LoggerKind.Grpc:
                    ConsumeFrameworkLog(
                        logLevel,
                        eventId,
                        state,
                        exception,
                        formatter,
                        "GrpcFrameworkLog");
                    break;
                case LoggerKind.Http:
                    ConsumeFrameworkLog(
                        logLevel,
                        eventId,
                        state,
                        exception,
                        formatter,
                        "HttpFrameworkLog");
                    break;
                case LoggerKind.Readiness:
                    ConsumeReadinessLog(eventId, state, exception);
                    break;
                case LoggerKind.Relay:
                    ConsumeRelayLog(eventId, state);
                    break;
            }
        }

        private void ConsumeFrameworkLog<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter,
            string eventName)
        {
            string eventIdentifier = eventId.Name is { Length: > 0 }
                ? $"{eventId.Id}:{eventId.Name}"
                : eventId.Id.ToString();
            string message = $"{categoryName} [{eventIdentifier}] {formatter(state, exception)}";
            if (exception is not null)
            {
                message = $"{message}{Environment.NewLine}{exception}";
            }

            eventGenerator.LogProxyEvent(logLevel, eventName, message);
        }

        private void ConsumeAspNetCoreHostingLog<TState>(EventId eventId, TState state)
        {
            if (eventId.Id != RequestFinishedEventId
                || !string.Equals(
                    GetStateStringValue(state, "Path"),
                    WorkerAssignmentPath,
                    StringComparison.Ordinal)
                || !string.Equals(
                    GetStateStringValue(state, "Method"),
                    "PUT",
                    StringComparison.Ordinal))
            {
                return;
            }

            int statusCode = GetStateValue(state, "StatusCode", 0);
            if (statusCode is not (
                StatusCodes.Status201Created
                or StatusCodes.Status204NoContent
                or StatusCodes.Status400BadRequest
                or StatusCodes.Status409Conflict
                or StatusCodes.Status415UnsupportedMediaType
                or StatusCodes.Status503ServiceUnavailable))
            {
                return;
            }

            bool succeeded = statusCode is >= 200 and < 400;
            eventGenerator.LogProxyEvent(
                succeeded ? LogLevel.Information : LogLevel.Warning,
                succeeded ? "WorkerAssignmentRequestCompleted" : "WorkerAssignmentRequestRejected",
                $"Worker assignment request completed with HTTP status {statusCode}.");
        }

        private void ConsumeReadinessLog<TState>(
            EventId eventId,
            TState state,
            Exception? exception)
        {
            WorkerEndpointReadinessResult? result = eventId.Id switch
            {
                DestinationResolutionFailedEventId => WorkerEndpointReadinessResult.NameResolutionFailed,
                DestinationReadyEventId => WorkerEndpointReadinessResult.Ready,
                DestinationNotReadyEventId => ClassifyReadinessFailure(exception),
                _ => null
            };
            if (result is not { } readinessResult)
            {
                return;
            }

            int attempts = GetAttempts(state);
            bool isReady = readinessResult == WorkerEndpointReadinessResult.Ready;
            eventGenerator.LogProxyEvent(
                isReady ? LogLevel.Information : LogLevel.Warning,
                isReady ? "WorkerHttpReadinessCompleted" : "WorkerHttpReadinessFailed",
                $"Worker HTTP readiness completed with {readinessResult} after {attempts} attempts.");
        }

        private void ConsumeRelayLog<TState>(EventId eventId, TState state)
        {
            long sessionId = GetStateValue(state, "SessionId", 0L);
            if (eventId.Id == FunctionGroupNotAdvertisedEventId)
            {
                WorkerAssignmentState assignmentState = GetStateValue(
                    state,
                    "AssignmentState",
                    WorkerAssignmentState.Unassigned);
                eventGenerator.LogProxyEvent(
                    LogLevel.Warning,
                    "WorkerFunctionGroupNotAdvertised",
                    $"FunctionRpc session {sessionId} did not advertise FunctionGroupName because the worker assignment state was {assignmentState}.");
                return;
            }

            if (eventId.Id is RelayAttachmentAcceptedEventId or RelayAttachmentRejectedEventId)
            {
                FunctionRpcRelaySide attachmentSide = GetStateValue(
                    state,
                    "Side",
                    FunctionRpcRelaySide.Worker);
                if (eventId.Id == RelayAttachmentAcceptedEventId)
                {
                    eventGenerator.LogProxyEvent(
                        LogLevel.Information,
                        "RelayAttachmentAccepted",
                        $"FunctionRpc session {sessionId} accepted the {attachmentSide} attachment.");
                }
                else
                {
                    FunctionRpcRelayAttachmentFailure attachmentReason = GetStateValue(
                        state,
                        "Reason",
                        FunctionRpcRelayAttachmentFailure.Shutdown);
                    eventGenerator.LogProxyEvent(
                        LogLevel.Warning,
                        "RelayAttachmentRejected",
                        $"FunctionRpc session {sessionId} rejected the {attachmentSide} attachment because {attachmentReason}.");
                }

                return;
            }

            if (eventId.Id is WorkerStartStreamAcceptedEventId or WorkerStartStreamRejectedEventId)
            {
                if (eventId.Id == WorkerStartStreamAcceptedEventId)
                {
                    eventGenerator.LogProxyEvent(
                        LogLevel.Information,
                        "WorkerStartStreamAccepted",
                        $"FunctionRpc session {sessionId} accepted worker StartStream.");
                }
                else
                {
                    WorkerStartStreamFailure startStreamReason = GetStateValue(
                        state,
                        "Reason",
                        WorkerStartStreamFailure.InvalidMessage);
                    eventGenerator.LogProxyEvent(
                        LogLevel.Warning,
                        "WorkerStartStreamRejected",
                        $"FunctionRpc session {sessionId} rejected worker StartStream because {startStreamReason}.");
                }

                return;
            }

            if (eventId.Id == WorkerInitializationCompletedEventId)
            {
                eventGenerator.LogProxyEvent(
                    LogLevel.Information,
                    "WorkerInitializationCompleted",
                    $"FunctionRpc session {sessionId} completed worker initialization.");
                return;
            }

            if (eventId.Id is not (RelaySessionFaultedEventId or RelaySessionTerminatedEventId))
            {
                return;
            }

            FunctionRpcRelaySide? side =
                GetNullableStateValue<TState, FunctionRpcRelaySide>(state, "Side");
            FunctionRpcRelayTerminationReason reason = eventId.Id == RelaySessionFaultedEventId
                ? FunctionRpcRelayTerminationReason.Faulted
                : GetStateValue(
                    state,
                    "Reason",
                    FunctionRpcRelayTerminationReason.PeerClosed);
            string terminatingSide = side?.ToString() ?? "Proxy";
            bool failed = reason == FunctionRpcRelayTerminationReason.Faulted;
            eventGenerator.LogProxyEvent(
                failed ? LogLevel.Warning : LogLevel.Information,
                failed ? "RelaySessionFailed" : "RelaySessionTerminated",
                $"FunctionRpc session {sessionId} was terminated by {terminatingSide} because {reason}.");
        }

        private static int GetAttempts<TState>(TState state)
        {
            return GetStateValue(state, "Attempts", 0);
        }

        private static WorkerEndpointReadinessResult ClassifyReadinessFailure(Exception? exception)
        {
            return exception switch
            {
                SocketException { SocketErrorCode: SocketError.ConnectionRefused } =>
                    WorkerEndpointReadinessResult.ConnectionRefused,
                SocketException { SocketErrorCode: SocketError.TimedOut } =>
                    WorkerEndpointReadinessResult.Timeout,
                SocketException => WorkerEndpointReadinessResult.ConnectionFailed,
                _ => WorkerEndpointReadinessResult.Timeout
            };
        }

        private static TValue GetStateValue<TState, TValue>(
            TState state,
            string key,
            TValue defaultValue)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return defaultValue;
            }

            foreach (KeyValuePair<string, object?> value in values)
            {
                if (string.Equals(value.Key, key, StringComparison.Ordinal)
                    && value.Value is TValue result)
                {
                    return result;
                }
            }

            return defaultValue;
        }

        private static string? GetStateStringValue<TState>(TState state, string key)
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return null;
            }

            foreach (KeyValuePair<string, object?> value in values)
            {
                if (string.Equals(value.Key, key, StringComparison.Ordinal))
                {
                    return value.Value?.ToString();
                }
            }

            return null;
        }

        private static TValue? GetNullableStateValue<TState, TValue>(
            TState state,
            string key)
            where TValue : struct
        {
            if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
            {
                return null;
            }

            foreach (KeyValuePair<string, object?> value in values)
            {
                if (string.Equals(value.Key, key, StringComparison.Ordinal)
                    && value.Value is TValue result)
                {
                    return result;
                }
            }

            return null;
        }
    }
}
