// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

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
            var scopeFactory = _scriptHostService.Services.GetService<IServiceScopeFactory>();
            if (scopeFactory is not null)
            {
                httpContext.Features.Set<IServiceProvidersFeature>(new RequestServicesFeature(httpContext, scopeFactory));
            }

            return _next(httpContext);
        }
    }
}
