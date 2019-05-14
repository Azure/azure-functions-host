// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.Middleware
{
    public class HstsConfigurationMiddleware : IScriptJobHostMiddleware
    {
        private readonly IOptions<HostHstsOptions> _hostHstsOptions;
        private HstsMiddleware _hstsMiddleware;
        private RequestDelegate _next;
        private RequestDelegate _invoke;
        private double _configured = 0;
        private double _initialized = 0;

        public HstsConfigurationMiddleware(IOptions<HostHstsOptions> hostHstsOptions)
        {
            _hostHstsOptions = hostHstsOptions;
            _invoke = InvokeInitialization;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            await _invoke(httpContext);
        }

        private async Task InvokeInitialization(HttpContext httpContext)
        {
            if (Interlocked.CompareExchange(ref _initialized, 1, 0) == 0)
            {
                if (_hostHstsOptions.Value.IsEnabled)
                {
                    _hstsMiddleware = new HstsMiddleware(_next, _hostHstsOptions);
                    Interlocked.Exchange(ref _invoke, _hstsMiddleware.Invoke);
                }
                else
                {
                    Interlocked.Exchange(ref _invoke, _next);
                }
            }
            await _invoke(httpContext);
        }

        public void ConfigureRequestDelegate(RequestDelegate next)
        {
            if (Interlocked.CompareExchange(ref _configured, 1, 0) == 0)
            {
                _next = next;
            }
        }
    }
}