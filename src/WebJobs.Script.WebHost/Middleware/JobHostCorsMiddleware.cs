// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class JobHostCorsMiddleware : IJobHostHttpMiddleware
    {
        private readonly CorsMiddleware _corsMiddleware;

        public JobHostCorsMiddleware(IOptions<HostCorsOptions> hostCorsOptions, ICorsMiddlewareFactory middlewareFactory)
        {
            _corsMiddleware = middlewareFactory.CreateCorsMiddleware(InvokeNext, hostCorsOptions);
        }

        public Task Invoke(HttpContext context, RequestDelegate next)
        {
            context.Items.Add(ScriptConstants.CorsMiddlewareRequestDelegate, next);

            // policy is supplied in the factory
            return _corsMiddleware.Invoke(context, null);
        }

        private Task InvokeNext(HttpContext context)
        {
            if (context.Items.Remove(ScriptConstants.CorsMiddlewareRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
            {
                return next(context);
            }

            return Task.CompletedTask;
        }
    }
}
