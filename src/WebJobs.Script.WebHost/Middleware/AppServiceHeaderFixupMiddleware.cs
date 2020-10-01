// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class AppServiceHeaderFixupMiddleware
    {
        internal const string DisguisedHostHeader = "DISGUISED-HOST";
        internal const string HostHeader = "HOST";
        internal const string ForwardedProtocolHeader = "X-Forwarded-Proto";
        private readonly RequestDelegate _next;

        public AppServiceHeaderFixupMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public Task Invoke(HttpContext httpContext)
        {
            if (httpContext.Request.Headers.TryGetValue(DisguisedHostHeader, out StringValues value))
            {
                httpContext.Request.Headers[HostHeader] = value;
            }

            if (httpContext.Request.Headers.TryGetValue(ForwardedProtocolHeader, out value))
            {
                string scheme = value.FirstOrDefault();
                if (!string.IsNullOrEmpty(scheme))
                {
                    httpContext.Request.Scheme = scheme;
                }
            }

            return _next(httpContext);
        }
    }
}