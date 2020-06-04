// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Middleware
{
    public class HstsConfigurationMiddleware : IJobHostHttpMiddleware
    {
        private RequestDelegate _invoke;

        public HstsConfigurationMiddleware(IOptions<HostHstsOptions> hostHstsOptions)
        {
            RequestDelegate contextNext = context =>
            {
                if (context.Items.Remove(ScriptConstants.HstsMiddlewareRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
                {
                    return next(context);
                }
                else
                {
                    return Task.CompletedTask;
                }
            };

            if (hostHstsOptions.Value.IsEnabled)
            {
                var hstsMiddleware = new HstsMiddleware(contextNext, hostHstsOptions);
                _invoke = hstsMiddleware.Invoke;
            }
            else
            {
                _invoke = contextNext;
            }
        }

        public Task Invoke(HttpContext context, RequestDelegate next)
        {
            context.Items.Add(ScriptConstants.HstsMiddlewareRequestDelegate, next);
            return _invoke(context);
        }
    }
}