// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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
        private readonly IOptions<HostHstsOptions> _hostHstsOptions;
        private HstsMiddleware _hstsMiddleware;
        private RequestDelegate _next;
        private double _nextConfigured = 0;
        private InvokeDelegate _invoke;

        public HstsConfigurationMiddleware(IOptions<HostHstsOptions> hostHstsOptions)
        {
            _hostHstsOptions = hostHstsOptions;
            _invoke = InvokeIntialization;
        }

        private delegate Task InvokeDelegate(HttpContext httpContext, RequestDelegate next);

        public async Task Invoke(HttpContext context, RequestDelegate next)
        {
            await _invoke(context, next);
        }

        public async Task InvokeIntialized(HttpContext context, RequestDelegate next)
        {
            await _next(context);
        }

        private async Task InvokeIntialization(HttpContext httpContext, RequestDelegate next)
        {
            if (Interlocked.CompareExchange(ref _nextConfigured, 1, 0) == 0)
            {
                if (_hostHstsOptions.Value.IsEnabled)
                {
                    _hstsMiddleware = new HstsMiddleware(next, _hostHstsOptions);
                    Interlocked.Exchange(ref _next, _hstsMiddleware.Invoke);
                }
                else
                {
                    Interlocked.Exchange(ref _next, next);
                }

                Interlocked.Exchange(ref _invoke, InvokeIntialized);
            }
            await _invoke(httpContext, _next);
        }
    }
}