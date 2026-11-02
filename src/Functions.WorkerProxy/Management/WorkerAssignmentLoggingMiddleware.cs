// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Globalization;
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
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            LogResult(start, exception is BadHttpRequestException badRequest ? badRequest.StatusCode : null, exception);
            throw;
        }

        LogResult(start, context.Response.StatusCode);
    }

    private void LogResult(long start, int? statusCode, Exception? exception = null)
    {
        WorkerPodState state = stateManager.State;
        double durationMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        using IDisposable? scope = logger.BeginScope(new WorkerSystemLogContext
        {
            WorkerId = state.WorkerId,
            ProxySessionId = state.SessionId
        });
        if (statusCode is null or >= StatusCodes.Status500InternalServerError)
        {
            // Assignment errors can contain configuration values. Record only the exception type.
            Log.RequestFailed(logger, statusCode?.ToString(CultureInfo.InvariantCulture) ?? "unavailable",
                exception?.GetType().FullName ?? "none", durationMs, state.AssignmentState, state.IsWorkerReady);
        }
        else if (exception is null && statusCode is >= StatusCodes.Status200OK and < StatusCodes.Status400BadRequest)
        {
            Log.RequestCompleted(logger, statusCode.Value, durationMs, state.AssignmentState, state.IsWorkerReady);
        }
        else
        {
            Log.RequestRejected(logger, statusCode.Value, durationMs, state.AssignmentState, state.IsWorkerReady);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(
            0,
            LogLevel.Information,
            "Worker assignment request completed with HTTP status {StatusCode} in {DurationMs} ms. Assignment state: {AssignmentState}. Worker attached with ID: {WorkerReady}.",
            EventName = "WorkerAssignmentRequestCompleted")]
        public static partial void RequestCompleted(ILogger logger, int statusCode, double durationMs, WorkerAssignmentState assignmentState, bool workerReady);

        [LoggerMessage(
            1,
            LogLevel.Warning,
            "Worker assignment request completed with HTTP status {StatusCode} in {DurationMs} ms. Assignment state: {AssignmentState}. Worker attached with ID: {WorkerReady}.",
            EventName = "WorkerAssignmentRequestRejected")]
        public static partial void RequestRejected(ILogger logger, int statusCode, double durationMs, WorkerAssignmentState assignmentState, bool workerReady);

        [LoggerMessage(
            2,
            LogLevel.Error,
            "Worker assignment request failed in {DurationMs} ms. HTTP status: {StatusCode}. Exception type: {ExceptionType}. Assignment state: {AssignmentState}. Worker attached with ID: {WorkerReady}.",
            EventName = "WorkerAssignmentRequestFailed")]
        public static partial void RequestFailed(ILogger logger, string statusCode, string exceptionType, double durationMs, WorkerAssignmentState assignmentState, bool workerReady);
    }
}
