// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HstsConfigurationMiddlewareTests
    {
        [Fact]
        public async Task Invoke_hstsEnabled_AddsResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = true });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(hstsOptions);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers["Strict-Transport-Security"].ToString(), "max-age=2592000");
        }

        [Fact]
        public async Task Invoke_hstsDisabled_DoesNotAddResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = false });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(hstsOptions);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }

        [Fact]
        public async Task Invoke_hstsEnabled_AddsResponseHeaderWithCorrectValue()
        {
            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var options = new HostHstsOptions()
            {
                IsEnabled = true,
                MaxAge = new TimeSpan(10, 0, 0, 0)
            };
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(options);

            var middleware = new HstsConfigurationMiddleware(hstsOptions);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;

            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers["Strict-Transport-Security"].ToString(), "max-age=864000");
        }

        [Fact]
        public async Task Invoke_hstsDisabledByDefault()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions());

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(hstsOptions);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }
    }
}
