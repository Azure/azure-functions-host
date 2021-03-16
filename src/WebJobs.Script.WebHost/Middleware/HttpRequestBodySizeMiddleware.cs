// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using static Microsoft.Azure.WebJobs.Script.EnvironmentSettingNames;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    /// <summary>
    /// A middleware responsible for MaxRequestBodySize size configuration
    /// </summary>
    internal class HttpRequestBodySizeMiddleware
    {
        private readonly RequestDelegate _next;
        private RequestDelegate _invoke;
        private long _maxRequestBodySize;

        public HttpRequestBodySizeMiddleware(RequestDelegate next, IEnvironment environment)
        {
            _next = next;
            _invoke = (context) =>
            {
                if (!environment.IsPlaceholderModeEnabled())
                {
                    string bodySizeLimit = environment.GetEnvironmentVariable(FunctionsRequestBodySizeLimit);
                    if (long.TryParse(bodySizeLimit, out _maxRequestBodySize))
                    {
                        Interlocked.Exchange(ref _invoke, InvokeAfterSpecialization);
                        return _invoke(context);
                    }
                    else
                    {
                        Interlocked.Exchange(ref _invoke, _next.Invoke);
                    }
                }
                return _next.Invoke(context);
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
