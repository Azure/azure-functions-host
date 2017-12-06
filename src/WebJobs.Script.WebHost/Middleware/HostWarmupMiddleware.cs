// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostWarmupMiddleware
    {
        private readonly RequestDelegate _next;

        public HostWarmupMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (StandbyManager.IsWarmUpRequest(httpContext.Request))
            {
                var scriptHostManager = (WebScriptHostManager)httpContext.Items[ScriptConstants.AzureFunctionsHostManagerKey];
                await StandbyManager.WarmUp(httpContext.Request, scriptHostManager);
            }

            await _next.Invoke(httpContext);
        }
    }
}
