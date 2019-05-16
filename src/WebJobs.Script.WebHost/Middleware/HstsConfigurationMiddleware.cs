// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HstsConfigurationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;
        private HstsMiddleware _hstsMiddleware;
        private HostHstsOptions _hostHstsOptions;
        private static object _syncLock = new object();

        public HstsConfigurationMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment)
        {
            _next = next;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task Invoke(HttpContext httpContext, IOptions<HostHstsOptions> options)
        {
            if (!object.ReferenceEquals(_hostHstsOptions, options.Value))
            {
                lock (_syncLock)
                {
                    if (!object.ReferenceEquals(_hostHstsOptions, options.Value))
                    {
                        if (options.Value.IsEnabled)
                        {
                            _hstsMiddleware = new HstsMiddleware(_next, options);
                        }
                        _hostHstsOptions = options.Value;
                    }
                }
            }

            if (!_webHostEnvironment.InStandbyMode && _hostHstsOptions.IsEnabled)
            {
                await _hstsMiddleware.Invoke(httpContext);
            }
            else
            {
                await _next(httpContext);
            }
        }
    }
}