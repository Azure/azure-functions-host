// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class HostnameFixupMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly HostNameProvider _hostNameProvider;

        public HostnameFixupMiddleware(RequestDelegate next, HostNameProvider hostNameProvider)
        {
            _next = next;
            _hostNameProvider = hostNameProvider;
        }

        public Task Invoke(HttpContext context)
        {
            _hostNameProvider.Synchronize(context.Request, _logger);
            return _next.Invoke(context);
        }
    }
}
