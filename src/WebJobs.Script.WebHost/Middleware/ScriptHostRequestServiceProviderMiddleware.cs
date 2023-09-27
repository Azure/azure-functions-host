// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class ScriptHostRequestServiceProviderMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly WebJobsScriptHostService _scriptHostService;

        public ScriptHostRequestServiceProviderMiddleware(RequestDelegate next, WebJobsScriptHostService scriptHostService)
        {
            _next = next;
            _scriptHostService = scriptHostService;
        }

        public Task Invoke(HttpContext httpContext)
        {
            httpContext.RequestServices = _scriptHostService.Services;

            return _next(httpContext);
        }
    }
}
