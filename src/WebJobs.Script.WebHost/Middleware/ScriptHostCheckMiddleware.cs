using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class ScriptHostCheckMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoggerFactory _loggerFactory;

        public ScriptHostCheckMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _loggerFactory = loggerFactory;
        }

        public async Task Invoke(HttpContext httpContext, WebScriptHostManager manager)
        {
            // in standby mode, we don't want to wait for host start
            bool bypassHostCheck = WebScriptHostManager.InStandbyMode;

            if (!bypassHostCheck)
            {
                bool hostReady = await manager.DelayUntilHostReady(timeoutSeconds: 5);

                if (!hostReady)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await httpContext.Response.WriteAsync("Function host is not running.");

                    return;
                }
            }

            await _next.Invoke(httpContext);
        }
    }
}
