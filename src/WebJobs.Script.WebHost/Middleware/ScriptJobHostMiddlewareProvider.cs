// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class ScriptJobHostMiddlewareProvider
    {
        private readonly RequestDelegate _next;

        public ScriptJobHostMiddlewareProvider(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext, IEnumerable<IScriptJobHostMiddleware> hostMiddlewareCollection)
        {
            var middlewareCount = hostMiddlewareCollection.Count();
            if (middlewareCount > 0)
            {
                hostMiddlewareCollection.ElementAt(middlewareCount - 1).Next = _next;
            }

            for (int i = middlewareCount - 1; i >= 0; i--)
            {
                if (i > 0)
                {
                    hostMiddlewareCollection.ElementAt(i - 1).Next = hostMiddlewareCollection.ElementAt(i).Invoke;
                }
            }
            await hostMiddlewareCollection.ElementAt(0).Invoke(httpContext);
        }
    }
}
