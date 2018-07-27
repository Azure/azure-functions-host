// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private readonly IScriptHostManager _hostManager;

        public HostWarmupMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, IScriptHostManager hostManager)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
            _hostManager = hostManager;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (StandbyManager.IsWarmUpRequest(httpContext.Request, _webHostEnvironment))
            {
                await StandbyManager.WarmUp(httpContext.Request, _hostManager);
            }

            await _next.Invoke(httpContext);
        }
    }
}
