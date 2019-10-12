// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class CorsMiddlewareFactory : ICorsMiddlewareFactory
    {
        private readonly IOptions<CorsOptions> _corsOptions;

        public CorsMiddlewareFactory(IOptions<CorsOptions> corsOptions)
        {
            _corsOptions = corsOptions;
        }

        public CorsMiddleware CreateCorsMiddleware(RequestDelegate next, IOptions<HostCorsOptions> corsOptions)
        {
            CorsMiddleware middleware = null;
            if (corsOptions?.Value != null)
            {
                var corsService = new CorsService(_corsOptions);
                var policy = _corsOptions.Value.GetPolicy(_corsOptions.Value.DefaultPolicyName);
                middleware = new CorsMiddleware(next, corsService, policy);
            }

            return middleware;
        }
    }
}
