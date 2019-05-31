// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading;
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
            RequestDelegate invoke = async context =>
            {
                if (context.Items.Remove(ScriptConstants.HstsMiddlewareRequestDelegate, out object requestDelegate) && requestDelegate is RequestDelegate next)
                {
                    await next(context);
                }
            };

            if (hostHstsOptions.Value.IsEnabled)
            {
                var hstsMiddleware = new HstsMiddleware(invoke, hostHstsOptions);
                _invoke = hstsMiddleware.Invoke;
            }
            else
            {
                _invoke = invoke;
            }
        }

        public async Task Invoke(HttpContext context, RequestDelegate next)
        {
            context.Items.Add(ScriptConstants.HstsMiddlewareRequestDelegate, next);
            await _invoke(context);
        }
    }
}