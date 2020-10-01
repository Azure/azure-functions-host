// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class CustomHttpHeadersMiddleware : IJobHostHttpMiddleware
    {
        private readonly CustomHttpHeadersOptions _hostOptions;

        public CustomHttpHeadersMiddleware(IOptions<CustomHttpHeadersOptions> hostOptions)
        {
            _hostOptions = hostOptions.Value;
        }

        public Task Invoke(HttpContext context, RequestDelegate next)
        {
            if (_hostOptions.Any())
            {
                context.Response.OnStarting(() =>
                {
                    foreach (var header in _hostOptions)
                    {
                        context.Response.Headers.TryAdd(header.Key, header.Value);
                    }

                    return Task.CompletedTask;
                });
            }

            return next(context);
        }
    }
}
