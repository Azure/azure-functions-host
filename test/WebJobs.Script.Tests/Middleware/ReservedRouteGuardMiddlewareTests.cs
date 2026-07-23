// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Net;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class ReservedRouteGuardMiddlewareTests
    {
        [Theory]
        [InlineData("/admin")]
        [InlineData("/admin/")]
        [InlineData("/admin/foo")]
        [InlineData("/ADMIN/foo")]
        [InlineData("/runtime")]
        [InlineData("/runtime/")]
        [InlineData("/runtime/foo")]
        [InlineData("/runtime/x/y")]
        public async Task Invoke_ReservedPath_ReturnsNotFound(string path)
        {
            bool nextInvoked = false;
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            });
            var context = CreateContext(path);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Equal(0, context.Response.Body.Length);
            Assert.False(nextInvoked);
            Assert.Equal(middleware.InvokeEnforcement, middleware.InnerInvoke);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/api/foo")]
        [InlineData("/administrator")]
        [InlineData("/runtimefoo")]
        public async Task Invoke_NonReservedPath_InvokesNext(string path)
        {
            bool nextInvoked = false;
            var middleware = CreateMiddleware(context =>
            {
                nextInvoked = true;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            });
            var context = CreateContext(path);

            await middleware.Invoke(context);

            Assert.True(nextInvoked);
            Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("/admin/warmup")]
        [InlineData("/ADMIN/WARMUP")]
        public async Task Invoke_AdminWarmupOnSupportedSku_InvokesNext(string path)
        {
            bool nextInvoked = false;
            var middleware = CreateMiddleware(context =>
            {
                nextInvoked = true;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            });
            var context = CreateContext(path);
            context.Request.QueryString = new QueryString("?key=value");

            await middleware.Invoke(context);

            Assert.True(nextInvoked);
            Assert.Equal(StatusCodes.Status202Accepted, context.Response.StatusCode);
        }

        [Theory]
        [InlineData("/admin/warmup/")]
        [InlineData("/admin/warmup/foo")]
        public async Task Invoke_AdminWarmupDescendant_ReturnsNotFound(string path)
        {
            bool nextInvoked = false;
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            });
            var context = CreateContext(path);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Invoke_AdminWarmupOnConsumptionSku_ReturnsNotFound()
        {
            bool nextInvoked = false;
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = false
            };
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }, reservedRouteOptions: reservedRouteOptions);
            var context = CreateContext("/admin/warmup");

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextInvoked);
        }

        [Fact]
        public async Task Invoke_InPlaceholderMode_EnforcesWithoutRewiring()
        {
            bool nextInvoked = false;
            var standbyOptions = new StandbyOptions
            {
                InStandbyMode = true
            };
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }, standbyOptions: standbyOptions);
            var context = CreateContext("/admin/foo");

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextInvoked);
            Assert.Equal(middleware.InvokeBeforeSpecialization, middleware.InnerInvoke);
        }

        [Fact]
        public async Task Invoke_AfterSpecializationWithOptOut_RewiresToNext()
        {
            int nextInvocations = 0;
            RequestDelegate next = context =>
            {
                nextInvocations++;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            };
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = true,
                DisableReservedRouteEnforcement = true
            };
            var middleware = CreateMiddleware(next, reservedRouteOptions: reservedRouteOptions);

            await middleware.Invoke(CreateContext("/admin/foo"));
            reservedRouteOptions.DisableReservedRouteEnforcement = false;
            var secondContext = CreateContext("/runtime/foo");
            await middleware.Invoke(secondContext);

            Assert.Equal(2, nextInvocations);
            Assert.Equal(StatusCodes.Status202Accepted, secondContext.Response.StatusCode);
            Assert.Equal(next, middleware.InnerInvoke);
        }

        [Fact]
        public async Task Invoke_AfterSpecializationWithoutOptOut_RewiresToEnforcement()
        {
            bool nextInvoked = false;
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = true
            };
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }, reservedRouteOptions: reservedRouteOptions);

            await middleware.Invoke(CreateContext("/admin/foo"));
            reservedRouteOptions.DisableReservedRouteEnforcement = true;
            var secondContext = CreateContext("/runtime/foo");
            await middleware.Invoke(secondContext);

            Assert.Equal(StatusCodes.Status404NotFound, secondContext.Response.StatusCode);
            Assert.False(nextInvoked);
            Assert.Equal(middleware.InvokeEnforcement, middleware.InnerInvoke);
        }

        [Theory]
        [InlineData(true, null, true)]
        [InlineData(true, ScriptConstants.DynamicSku, false)]
        public void ReservedRouteOptionsSetup_ReturnsExpectedWarmupRouteValue(bool isWindows, string sku, bool expected)
        {
            var environment = new TestEnvironment
            {
                Platform = isWindows ? OSPlatform.Windows : OSPlatform.Linux
            };
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, sku);
            var options = new ReservedRouteOptions();

            new ReservedRouteOptionsSetup(environment).Configure(options);

            Assert.Equal(expected, options.AdminWarmupRouteEnabled);
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("OtherFeature", false)]
        [InlineData(ScriptConstants.FeatureFlagDisableReservedRouteEnforcement, true)]
        public void ReservedRouteOptionsSetup_ReturnsExpectedFeatureFlagValue(string featureFlags, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsFeatureFlags, featureFlags);
            var options = new ReservedRouteOptions();

            new ReservedRouteOptionsSetup(environment).Configure(options);

            Assert.Equal(expected, options.DisableReservedRouteEnforcement);
        }

        private static ReservedRouteGuardMiddleware CreateMiddleware(
            RequestDelegate next,
            StandbyOptions standbyOptions = null,
            ReservedRouteOptions reservedRouteOptions = null)
        {
            return new ReservedRouteGuardMiddleware(
                next,
                new TestOptionsMonitor<StandbyOptions>(standbyOptions),
                new TestOptionsMonitor<ReservedRouteOptions>(
                    reservedRouteOptions ?? new ReservedRouteOptions { AdminWarmupRouteEnabled = true }),
                Mock.Of<ILogger<ReservedRouteGuardMiddleware>>());
        }

        private static DefaultHttpContext CreateContext(string path)
        {
            return new DefaultHttpContext
            {
                Request =
                {
                    Path = path
                }
            };
        }
    }
}
