// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class CorsConfigurationMiddleware : IJobHostHttpMiddleware
    {
        private RequestDelegate _invoke;

        public CorsConfigurationMiddleware(IOptions<HostCorsOptions> hostCorsOptions, ICorsMiddlewareFactory middlewareFactory)
        {
            RequestDelegate contextNext = async context =>
            {
                if (context.Items.Remove(ScriptConstants.CorsMiddlewareRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
                {
                    await next(context);
                }
            };

            var corsMiddleware = middlewareFactory.CreateCorsMiddleware(contextNext, hostCorsOptions);
            if (corsMiddleware != null)
            {
                _invoke = corsMiddleware.Invoke;
            }
            else
            {
                _invoke = contextNext;
            }
        }

        public async Task Invoke(HttpContext context, RequestDelegate next)
        {
            context.Items.Add(ScriptConstants.CorsMiddlewareRequestDelegate, next);
            await _invoke(context);
        }
    }
}
