// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        private readonly WebScriptHostManager _scriptHostManager;

        public ScriptHostCheckMiddleware(RequestDelegate next, WebScriptHostManager scriptHostManager, ILoggerFactory loggerFactory)
        {
            _next = next;
            _scriptHostManager = scriptHostManager;
            _loggerFactory = loggerFactory;
        }

        public async Task Invoke(HttpContext httpContext, WebScriptHostManager manager)
        {
            // in standby mode, we don't want to wait for host start
            bool bypassHostCheck = WebScriptHostManager.InStandbyMode;

            if (!bypassHostCheck)
            {
                bool hostReady = await manager.DelayUntilHostReady();

                if (!hostReady)
                {
                    httpContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                    await httpContext.Response.WriteAsync("Function host is not running.");

                    return;
                }
            }

            if (StandbyManager.IsWarmUpRequest(httpContext.Request))
            {
                await StandbyManager.WarmUp(httpContext.Request, _scriptHostManager);
            }

            await _next.Invoke(httpContext);
        }
    }
}
