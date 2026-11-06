// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Resolves the worker endpoint, waits for readiness, and forwards eligible requests through YARP.
/// </summary>
internal sealed partial class WorkerHttpForwardingMiddleware(
    IOptions<WorkerProxyOptions> options,
    WorkerEndpointReadinessProbe readinessProbe,
    WorkerHttpForwarder forwarder,
    FunctionRpcRelay relay,
    WorkerPodStateManager stateManager,
    ILogger<WorkerHttpForwardingMiddleware> logger)
{
    private readonly FunctionRpcRelay _relay = relay ?? throw new ArgumentNullException(nameof(relay));

    /// <summary>
    /// Forwards a request to the frozen worker destination or the configured standalone override.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        long start = Stopwatch.GetTimestamp();
        var (workerDestination, snapshot) = _relay.GetHttpForwardingSnapshot();
        using IDisposable? scope = logger.BeginScope(snapshot with { RequestId = context.TraceIdentifier });
        if (stateManager.State.RemovalRequested)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            LogOutcome("Admission", "Rejected", "RemovalRequested");
            return;
        }

        Uri? destination = workerDestination
            ?? WorkerHttpDestinationResolver.Resolve(options.Value.WorkerHttpEndpoint, advertisedEndpoint: null);

        if (destination is null)
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            WorkerHttpForwardingTelemetry.RecordDestinationNotConfigured();
            LogOutcome("Admission", "Rejected", "DestinationNotConfigured");
            return;
        }

        if (!readinessProbe.IsKnownReady(destination))
        {
            WorkerEndpointReadinessResult readinessResult;
            try
            {
                readinessResult = await readinessProbe.WaitForReadyAsync(destination, context.RequestAborted);
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                LogOutcome("Readiness", "Canceled", "CallerCanceled", cancellationSource: "Caller");
                throw;
            }

            if (readinessResult is not WorkerEndpointReadinessResult.Ready)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                WorkerHttpForwardingTelemetry.RecordDestinationNotReady(readinessResult);
                return;
            }
        }

        ForwarderError error;
        try
        {
            error = await forwarder.ForwardAsync(context, destination);
        }
        catch (Exception exception)
        {
            bool canceled = exception is OperationCanceledException;
            string? source = canceled ? context.RequestAborted.IsCancellationRequested ? "Caller" : "Unknown" : null;
            LogOutcome("Forwarding", canceled ? "Canceled" : "TransportFailure",
                canceled ? "ForwardingCanceled" : "ForwardingException", cancellationSource: source);
            throw;
        }

        if (context.RequestAborted.IsCancellationRequested)
        {
            LogOutcome("Forwarding", "Canceled", "CallerCanceled", error, "Caller");
            return;
        }

        if (error is ForwarderError.None)
        {
            if (context.Response.StatusCode >= StatusCodes.Status500InternalServerError)
            {
                LogOutcome("Forwarding", "WorkerServerError", "WorkerStatus", error);
            }

            return;
        }

        readinessProbe.Invalidate(destination);
        if (!context.Response.HasStarted)
        {
            context.Response.StatusCode = StatusCodes.Status502BadGateway;
        }

        WorkerHttpForwardingTelemetry.RecordForwarderError(error);
        LogOutcome("Forwarding", "TransportFailure", "ForwarderError", error);

        void LogOutcome(string phase, string outcome, string reason, ForwarderError? error = null, string? cancellationSource = null)
        {
            using IDisposable? outcomeScope = logger.BeginScope(new WorkerSystemLogContext
            {
                Operation = "Http",
                Phase = phase,
                Outcome = outcome,
                ReasonCode = reason,
                StatusCode = context.Response.StatusCode,
                ResponseStarted = context.Response.HasStarted,
                ElapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                ForwarderError = error?.ToString(),
                CancellationSource = cancellationSource
            });
            if (string.Equals(cancellationSource, "Caller", StringComparison.Ordinal))
            {
                Log.WorkerHttpOutcome(logger, LogLevel.Information, outcome, phase, context.Response.StatusCode);
            }
            else
            {
                Log.WorkerHttpOutcome(logger, LogLevel.Warning, outcome, phase, context.Response.StatusCode);
            }
        }
    }

    private static partial class Log
    {
        [LoggerMessage(EventId = 0, Message = "Worker HTTP {Outcome} during {Phase}; observed status {StatusCode}.")]
        public static partial void WorkerHttpOutcome(ILogger logger, LogLevel level, string outcome, string phase, int statusCode);
    }
}
