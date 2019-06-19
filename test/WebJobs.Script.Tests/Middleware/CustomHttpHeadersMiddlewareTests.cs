// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class CustomHttpHeadersMiddlewareTests
    {
        [Fact]
        public async Task Invoke_hasCustomHeaders_AddsResponseHeaders()
        {
            var headers = new CustomHttpHeadersOptions
            {
                { "X-Content-Type-Options", "nosniff" },
                { "Feature-Policy", "camera 'none'; geolocation 'none'" }
            };
            var headerOptions = new OptionsWrapper<CustomHttpHeadersOptions>(headers);

            bool nextInvoked = false;
            RequestDelegate next = (context) =>
            {
                nextInvoked = true;
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new CustomHttpHeadersMiddleware(headerOptions);

            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers["X-Content-Type-Options"].ToString(), "nosniff");
            Assert.Equal(httpContext.Response.Headers["Feature-Policy"].ToString(), "camera 'none'; geolocation 'none'");
        }

        [Fact]
        public async Task Invoke_noCustomHeaders_DoesNotAddResponseHeader()
        {
            var headerOptions = new OptionsWrapper<CustomHttpHeadersOptions>(new CustomHttpHeadersOptions());

            bool nextInvoked = false;
            RequestDelegate next = (context) =>
            {
                nextInvoked = true;
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new CustomHttpHeadersMiddleware(headerOptions);

            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }
    }
}
