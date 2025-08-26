// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Models;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks
{
    public sealed class HealthCheckWaitMiddleware(RequestDelegate next, IScriptHostManager manager)
    {
        private const int MaxWaitSeconds = 60;
        private readonly RequestDelegate _next = next ?? throw new ArgumentNullException(nameof(next));
        private readonly IScriptHostManager _manager = manager ?? throw new ArgumentNullException(nameof(manager));

        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // If specified, the ?wait={seconds} query param will wait for an
            // active script host for that duration. This is to avoid excessive polling
            // when waiting for the initial readiness probe.
            if (context.Request.Query.TryGetValue("wait", out StringValues wait))
            {
                if (!int.TryParse(wait.ToString(), out int waitSeconds) || waitSeconds < 0)
                {
                    context.Response.StatusCode = StatusCodes.Status400BadRequest;
                    await context.Response.WriteAsJsonAsync(
                        ErrorResponse.BadArgument("'wait' query param must be a positive integer", $"wait={wait}"));
                    return;
                }

                waitSeconds = Math.Min(waitSeconds, MaxWaitSeconds);
                await _manager.DelayUntilHostReadyAsync(waitSeconds).WaitAsync(context.RequestAborted);
            }

            await _next(context);
        }
    }
}
