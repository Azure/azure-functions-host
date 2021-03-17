// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HttpRequestBodySizeMiddlewareTests
    {
        [Theory]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData("invalid", false)]
        [InlineData("1024", true)]
        public async Task MaxRequestBodySizeLimit_RequestBodySizeLimit_SetExpectedValue(string requestBodySizeLimit, bool validData)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRequestBodySizeLimit, requestBodySizeLimit);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "0");

            bool nextInvoked = false;
            long? configuredLimit = 0;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                configuredLimit = ctxt.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize;
                return Task.CompletedTask;
            };

            var middleware = new HttpRequestBodySizeMiddleware(next, environment);

            var httpContext = new DefaultHttpContext();

            httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestHttpMaxRequestBodySizeFeature());
            await middleware.Invoke(httpContext);
            Assert.True(nextInvoked);

            if (validData)
            {
                Assert.Equal(configuredLimit, long.Parse(requestBodySizeLimit));
            }
            else
            {
                Assert.Equal(configuredLimit, ScriptConstants.DefaultMaxRequestBodySize);
            }
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("invalid")]
        [InlineData("1024")]
        public async Task MaxRequestBodySizeLimit_InPlaceHolderMode_DoesNotUpdateConfiguration(string requestBodySizeLimit)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRequestBodySizeLimit, requestBodySizeLimit);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");

            bool nextInvoked = false;
            long? configuredLimit = 0;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                configuredLimit = ctxt.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize;
                return Task.CompletedTask;
            };

            var middleware = new HttpRequestBodySizeMiddleware(next, environment);

            var httpContext = new DefaultHttpContext();

            httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestHttpMaxRequestBodySizeFeature());
            await middleware.Invoke(httpContext);
            Assert.True(nextInvoked);
            Assert.Equal(configuredLimit, ScriptConstants.DefaultMaxRequestBodySize);
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public async Task MaxRequestBodySizeLimit_MiddleWareInvoke_GetWiredUpCorrectly(bool inPlaceHolderMode, bool isLimitSet)
        {
            bool nextInvoked = false;
            long? configuredLimit = 0;
            RequestDelegate next = (ctxt) =>
            {
                nextInvoked = true;
                ctxt.Response.StatusCode = (int)HttpStatusCode.Accepted;
                configuredLimit = ctxt.Features.Get<IHttpMaxRequestBodySizeFeature>().MaxRequestBodySize;
                return Task.CompletedTask;
            };

            var environment = new TestEnvironment();

            string placeHolderModeValue = inPlaceHolderMode ? "1" : "0";
            string limit = isLimitSet ? "100" : "invalid";

            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, placeHolderModeValue);
            environment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRequestBodySizeLimit, limit);

            var middleware = new HttpRequestBodySizeMiddleware(next, environment);
            var httpContext = new DefaultHttpContext();

            httpContext.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestHttpMaxRequestBodySizeFeature());
            await middleware.Invoke(httpContext);
            Assert.True(nextInvoked);

            if (inPlaceHolderMode)
            {
                Assert.Equal(middleware.InnerInvoke, middleware.InvokeBeforeSpecialization);
            }

            if (!inPlaceHolderMode && isLimitSet)
            {
                Assert.Equal(middleware.InnerInvoke, middleware.InvokeAfterSpecialization);
            }

            if (!inPlaceHolderMode && !isLimitSet)
            {
                Assert.Equal(middleware.InnerInvoke, next);
            }
        }

        public class TestHttpMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
        {
            private long? _maxRequestBodySize = ScriptConstants.DefaultMaxRequestBodySize;

            public bool IsReadOnly => false;

            long? IHttpMaxRequestBodySizeFeature.MaxRequestBodySize { get => _maxRequestBodySize; set => _maxRequestBodySize = value; }
        }
    }
}
