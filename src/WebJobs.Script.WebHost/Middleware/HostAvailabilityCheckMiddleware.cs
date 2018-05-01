// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public partial class HostAvailabilityCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<HostAvailabilityCheckMiddleware> _logger;

        public HostAvailabilityCheckMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _logger = loggerFactory.CreateLogger<HostAvailabilityCheckMiddleware>();
        }

        public async Task Invoke(HttpContext httpContext, WebHostResolver resolver)
        {
            using (Logger.VerifyingHostAvailabilityScope(_logger, httpContext.TraceIdentifier))
            {
                Logger.InitiatingHostAvailabilityCheck(_logger);

                bool hostReady = await WebScriptHostManager.DelayUntilHostReady(resolver);
                if (!hostReady)
                {
                    Logger.HostUnavailableAfterCheck(_logger);

                    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await httpContext.Response.WriteAsync("Function host is not running.");

                    return;
                }

                Logger.HostAvailabilityCheckSucceeded(_logger);

                await _next.Invoke(httpContext);
            }
        }
    }
}