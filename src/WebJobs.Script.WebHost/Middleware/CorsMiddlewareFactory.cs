// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class CorsMiddlewareFactory : ICorsMiddlewareFactory
    {
        private readonly IOptions<CorsOptions> _corsOptions;
        private readonly ILoggerFactory _loggerFactory;

        public CorsMiddlewareFactory(IOptions<CorsOptions> corsOptions, ILoggerFactory loggerFactory)
        {
            _corsOptions = corsOptions;
            _loggerFactory = loggerFactory;
        }

        public CorsMiddleware CreateCorsMiddleware(RequestDelegate next, IOptions<HostCorsOptions> corsOptions)
        {
            CorsPolicy policy = _corsOptions.Value.GetPolicy(_corsOptions.Value.DefaultPolicyName);
            var corsService = new CorsService(_corsOptions, _loggerFactory);
            var middleware = new CorsMiddleware(next, corsService, policy, _loggerFactory);

            return middleware;
        }
    }
}
