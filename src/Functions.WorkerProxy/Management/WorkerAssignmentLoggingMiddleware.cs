// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.Functions.WorkerProxy.Management;

internal sealed partial class WorkerAssignmentLoggingMiddleware(
    RequestDelegate next,
    ILogger<WorkerAssignmentLoggingMiddleware> logger)
{
    private const string WorkerAssignmentPath = "/admin/worker/assignment";

    public async Task InvokeAsync(HttpContext context)
    {
        await next(context);

        if (!HttpMethods.IsPut(context.Request.Method)
            || !string.Equals(
                context.Request.Path.Value,
                WorkerAssignmentPath,
                StringComparison.Ordinal))
        {
            return;
        }

        int statusCode = context.Response.StatusCode;
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
