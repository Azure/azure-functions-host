// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// A middleware responsible for MaxRequestBodySize size configuration
    /// </summary>
    internal class HttpRequestSizeMiddleware
    {
        private const long DefaultRequestBodySize = 104857600;
        private readonly RequestDelegate _next;
        private readonly IEnvironment _environment;
        private RequestDelegate _invoke;
        private long _maxRequestBodySize;

        public HttpRequestSizeMiddleware(RequestDelegate next, IEnvironment environment)
        {
            _next = next;
            _environment = environment;
            _invoke = (context) =>
            {
                if (!environment.IsPlaceholderModeEnabled())
                {
                    _maxRequestBodySize = _environment.GetFunctionsRequestBodySizeLimit() ?? DefaultRequestBodySize;
                    Interlocked.Exchange(ref _invoke, InvokeAfterSpecialization);
                    return _invoke(context);
                }
                else
                {
                    return next(context);
                }
            };
        }

        public Task Invoke(HttpContext httpContext)
        {
            return _invoke(httpContext);
        }

        private Task InvokeAfterSpecialization(HttpContext httpContext)
        {
            httpContext.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize = _maxRequestBodySize;
            return _next.Invoke(httpContext);
        }
    }
}
