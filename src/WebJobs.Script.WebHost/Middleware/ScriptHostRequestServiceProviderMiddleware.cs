// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting.Internal;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class ScriptHostRequestServiceProviderMiddleware
    {
        private readonly RequestDelegate _next;

        public ScriptHostRequestServiceProviderMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, WebJobsScriptHostService manager)
        {
            // TODO: DI (FACAVAL) Remove this once the host check middleware is updated.
            //await Task.Delay(5000);

            if (manager.Services is IServiceScopeFactory scopedServiceProvider)
            {
                var features = httpContext.Features;
                var servicesFeature = features.Get<IServiceProvidersFeature>();
                features.Set<IServiceProvidersFeature>(new RequestServicesFeature(httpContext, scopedServiceProvider));
            }

            await _next(httpContext);
        }
    }
}
