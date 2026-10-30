// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.Rpc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Yarp.ReverseProxy.Forwarder;

namespace Azure.Functions.WorkerProxy.Http;

/// <summary>
/// Resolves the worker endpoint, waits for readiness, and forwards eligible requests through YARP.
/// </summary>
internal sealed class WorkerHttpForwardingMiddleware(
    IOptions<WorkerProxyOptions> options,
    WorkerEndpointReadinessProbe readinessProbe,
    WorkerHttpForwarder forwarder,
    FunctionRpcRelay relay,
    ILogger<WorkerHttpForwardingMiddleware>? logger = null)
{
    private readonly FunctionRpcRelay _relay = relay ?? throw new ArgumentNullException(nameof(relay));
    private readonly ILogger _logger = logger ?? NullLogger<WorkerHttpForwardingMiddleware>.Instance;

    /// <summary>
    /// Forwards a request to the frozen worker destination or the configured standalone override.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        (Uri? advertisedDestination, WorkerSystemLogContext relayContext) = _relay.GetHttpForwardingContext();
        WorkerSystemLogContext logContext = relayContext with
        {
            InvocationId = context.Request.Headers["x-ms-invocation-id"].ToString(),
            Operation = "HttpForwarding"
        };
        using IDisposable? scope = _logger.BeginScope(logContext);
        long start = Stopwatch.GetTimestamp();
        string outcome = "Failed";
        string? reason = null;
        ForwarderError? forwardingError = null;
        using (_logger.BeginScope(logContext with { Stage = "Started" }))
        {
            _logger.LogDebug(new EventId(0, "HttpForwardingStarted"), "HTTP forwarding started.");
        }

        try
        {
            Uri? destination = advertisedDestination
                ?? WorkerHttpDestinationResolver.Resolve(options.Value.WorkerHttpEndpoint, advertisedEndpoint: null);

            if (destination is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                outcome = "Rejected";
                reason = "DestinationNotConfigured";
                WorkerHttpForwardingTelemetry.RecordDestinationNotConfigured();
                return;
            }

            if (!readinessProbe.IsKnownReady(destination))
            {
                WorkerEndpointReadinessResult readinessResult =
                    await readinessProbe.WaitForReadyAsync(destination, context.RequestAborted);

                if (readinessResult is not WorkerEndpointReadinessResult.Ready)
                {
                    context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    outcome = "Rejected";
                    reason = readinessResult.ToString();
                    WorkerHttpForwardingTelemetry.RecordDestinationNotReady(readinessResult);
                    return;
                }
            }

            ForwarderError error = await forwarder.ForwardAsync(context, destination);
            forwardingError = error;
            if (context.RequestAborted.IsCancellationRequested)
            {
                outcome = "Canceled";
                return;
            }

            if (error is ForwarderError.None)
            {
                outcome = "Succeeded";
                return;
            }

            readinessProbe.Invalidate(destination);
            if (!context.Response.HasStarted)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
            }

            reason = "ForwarderError";
            WorkerHttpForwardingTelemetry.RecordForwarderError(error);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            outcome = "Canceled";
            throw;
        }
        finally
        {
            using IDisposable? completedScope = _logger.BeginScope(logContext with
            {
                Stage = "Completed",
                Outcome = outcome,
                ReasonCode = reason,
                DurationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                Attributes = new()
                {
                    HttpStatusCode = context.Response.StatusCode,
                    ForwarderError = forwardingError?.ToString()
                }
            });
            _logger.Log(outcome is "Failed" or "Rejected" ? LogLevel.Warning : LogLevel.Debug,
                new EventId(1, "HttpForwardingCompleted"),
                "HTTP forwarding completed with outcome {Outcome} and HTTP status {HttpStatusCode}.",
                outcome, context.Response.StatusCode);
        }
    }
}
