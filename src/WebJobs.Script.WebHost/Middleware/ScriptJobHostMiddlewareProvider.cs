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

        public async Task Invoke(HttpContext httpContext, IScriptJobHostMiddleware scriptJobHostMiddleware)
        {
            scriptJobHostMiddleware.ConfigureRequestDelegate(_next);
            await scriptJobHostMiddleware.Invoke(httpContext);
        }
    }
}
