// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Azure.Functions.WorkerProxy.Logging;
using Azure.Functions.WorkerProxy.State;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Management;

internal sealed partial class WorkerAssignmentLoggingMiddleware(
    RequestDelegate next,
    ILogger<WorkerAssignmentLoggingMiddleware> logger,
    WorkerPodStateManager stateManager)
{
    private const string WorkerAssignmentPath = "/admin/worker/assignment";

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPut(context.Request.Method)
            || context.GetEndpoint() is not RouteEndpoint endpoint
            || !string.Equals(
                endpoint.RoutePattern.RawText,
                WorkerAssignmentPath,
                StringComparison.Ordinal))
        {
            await next(context);
            return;
        }

        long start = Stopwatch.GetTimestamp();
        await next(context);
        WorkerPodState state = stateManager.State;
        int statusCode = context.Response.StatusCode;
        using IDisposable? scope = logger.BeginScope(new WorkerSystemLogContext
        {
            WorkerId = state.WorkerId,
            ProxySessionId = state.SessionId,
            Operation = "Assignment",
            Stage = "Completed",
            Outcome = statusCode is >= 200 and < 400 ? "Succeeded" : "Rejected",
            DurationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            Attributes = new()
            {
                HttpStatusCode = statusCode,
                StateRevision = state.Revision,
                WorkerReady = state.IsWorkerReady,
                AssignmentState = state.AssignmentState.ToString()
            }
        });
        if (statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest)
        {
            Log.RequestCompleted(logger, statusCode);
        }
        else
        {
            Log.RequestRejected(logger, statusCode);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            0,
            LogLevel.Information,
            "Worker assignment request completed with HTTP status {StatusCode}.",
            EventName = "WorkerAssignmentRequestCompleted")]
        public static partial void RequestCompleted(ILogger logger, int statusCode);

        [LoggerMessage(
            1,
            LogLevel.Warning,
            "Worker assignment request completed with HTTP status {StatusCode}.",
            EventName = "WorkerAssignmentRequestRejected")]
        public static partial void RequestRejected(ILogger logger, int statusCode);
    }
}
