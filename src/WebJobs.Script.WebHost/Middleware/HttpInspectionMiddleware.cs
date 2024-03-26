// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.Workers;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// A middleware responsible for the execution of the Job Host scoped middleware pipeline.
    /// </summary>
    internal class HttpInspectionMiddleware
    {
        private readonly RequestDelegate _next;

        public HttpInspectionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            if (_next != null)
            {
                await _next(httpContext);
            }

            var statusCode = httpContext.Response.StatusCode;
            Console.WriteLine("Status code in host middleware is " + statusCode);

            if (statusCode != 400)
            {
                Console.WriteLine("Status code in host middleware is not 400, it's " + statusCode);

                if (httpContext.Response.ContentLength == 0)
                {
                    Console.WriteLine("Empty");
                }
            }

            return;
        }
    }
}
