// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HstsConfigurationMiddlewareTests
    {
        [Fact]
        public async Task Invoke_hstsEnabled_NotInStandbyMode_AddsResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = true });
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = false });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers["Strict-Transport-Security"].ToString(), "max-age=2592000");
        }

        [Fact]
        public async Task Invoke_hstsDisabledNotInStandbyMode_DoesNotAddsResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = false });
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = false });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }

        [Fact]
        public async Task Invoke_hstsEnabledInStandbyMode_DoesNotAddResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = true });
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = true });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }

        [Fact]
        public async Task Invoke_hstsDisabledInStandbyMode_DoesNotAddResponseHeader()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions() { IsEnabled = true });
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = true });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }

        [Fact]
        public async Task Invoke_hstsEnabled_NotInStandbyMode_AddsResponseHeaderWithCorrectValue()
        {
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = false });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            var options = new HostHstsOptions()
            {
                IsEnabled = true,
                MaxAge = new TimeSpan(10, 0, 0, 0)
            };
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(options);

            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers["Strict-Transport-Security"].ToString(), "max-age=864000");
        }

        [Fact]
        public async Task Invoke_hstsDisabledByDefault()
        {
            var hstsOptions = new OptionsWrapper<HostHstsOptions>(new HostHstsOptions());
            var standbyOptions = new OptionsWrapper<StandbyOptions>(new StandbyOptions() { InStandbyMode = false });

            bool nextInvoked = false;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new HstsConfigurationMiddleware(next);

            var httpContext = new DefaultHttpContext();
            httpContext.Request.IsHttps = true;
            await middleware.Invoke(httpContext, hstsOptions, standbyOptions);
            Assert.True(nextInvoked);
            Assert.Equal(httpContext.Response.Headers.Count, 0);
        }
    }
}
