// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

        [Theory]
        [InlineData("/admin/warmup/")]
        [InlineData("/ADMIN/WARMUP/")]
        public async Task Invoke_AdminWarmupTrailingSlash_DoesNotLogHostWarmupRouteCollision(string path)
        {
            var (router, routeHandler) = CreateRouter("admin/warmup", "Warmup");
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router);
            var context = CreateContext(path, logger);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Empty(logger.GetLogMessages());
            routeHandler.Verify(p => p.InvokeAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_AdminWarmupTrailingSlashOnUnsupportedSku_LogsCustomerCollision()
        {
            const string functionName = "ReservedRouteCatchAll";
            var (router, routeHandler) = CreateRouter("{*route}", functionName);
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = false
            };
            var middleware = CreateMiddleware(
                _ => Task.CompletedTask,
                reservedRouteOptions: reservedRouteOptions,
                router: router);
            var context = CreateContext("/admin/warmup/", logger);

            await middleware.Invoke(context);

            LogMessage log = Assert.Single(logger.GetLogMessages());
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.Contains($"function '{functionName}'", log.FormattedMessage, StringComparison.Ordinal);
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            routeHandler.Verify(p => p.InvokeAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
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
        public async Task Invoke_InPlaceholderMode_EnforcesDespiteOptOut()
        {
            bool nextInvoked = false;
            var (router, routeHandler) = CreateRouter("{*route}", "ReservedRouteCatchAll");
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var standbyOptions = new StandbyOptions
            {
                InStandbyMode = true
            };
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = true,
                DisableReservedRouteEnforcement = true
            };
            var middleware = CreateMiddleware(_ =>
            {
                nextInvoked = true;
                return Task.CompletedTask;
            }, standbyOptions: standbyOptions, reservedRouteOptions: reservedRouteOptions, router: router);
            var context = CreateContext("/admin/foo", logger);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.False(nextInvoked);
            Assert.Empty(logger.GetLogMessages());
            routeHandler.Verify(p => p.InvokeAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_ReservedPathMatchingFunction_LogsErrorOnceWithoutInvokingRouteHandler()
        {
            const string functionName = "ReservedRouteCatchAll";
            var (router, routeHandler) = CreateRouter("{*route}", functionName);
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router);
            var context = CreateContext("/admin/foo", logger);
            var secondContext = CreateContext("/runtime/foo", logger);

            await middleware.Invoke(context);
            await middleware.Invoke(secondContext);

            LogMessage log = Assert.Single(logger.GetLogMessages());
            Assert.Equal(LogLevel.Error, log.Level);
            Assert.Equal(
                $"The request path '/admin/foo' was rejected because it uses a reserved host route and matches the route for function '{functionName}'. Update the function route to use a non-reserved path.",
                log.FormattedMessage);
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Assert.Equal(StatusCodes.Status404NotFound, secondContext.Response.StatusCode);
            routeHandler.Verify(p => p.InvokeAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_ReservedPathWithoutMatchingFunction_DoesNotLog()
        {
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(_ => Task.CompletedTask);
            var context = CreateContext("/admin/foo", logger);

            await middleware.Invoke(context);

            Assert.Empty(logger.GetLogMessages());
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_ReservedPathWithMethodMismatch_DoesNotLog()
        {
            var (router, routeHandler) = CreateRouter("{*route}", "ReservedRouteCatchAll", HttpMethods.Post);
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router);
            var context = CreateContext("/runtime/foo", logger);

            await middleware.Invoke(context);

            Assert.Empty(logger.GetLogMessages());
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            routeHandler.Verify(p => p.InvokeAsync(It.IsAny<HttpContext>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_HostNotRunning_DoesNotProbeOrLog()
        {
            var router = new Mock<IWebJobsRouter>(MockBehavior.Strict);
            var scriptHostManager = Mock.Of<IScriptHostManager>(p => p.State == ScriptHostState.Default);
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(
                _ => Task.CompletedTask,
                router: router.Object,
                scriptHostManager: scriptHostManager);
            var context = CreateContext("/admin/foo", logger);

            await middleware.Invoke(context);

            Assert.Empty(logger.GetLogMessages());
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            router.Verify(p => p.RouteAsync(It.IsAny<RouteContext>()), Times.Never);
        }

        [Fact]
        public async Task Invoke_RouteProbeThrows_ReturnsNotFoundAndLogsWarning()
        {
            var router = new Mock<IWebJobsRouter>();
            router
                .Setup(p => p.RouteAsync(It.IsAny<RouteContext>()))
                .ThrowsAsync(new InvalidOperationException("Route probe failed."));
            var logger = new TestLogger<ReservedRouteGuardMiddleware>();
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router.Object);
            var context = CreateContext("/admin/foo", logger);

            await middleware.Invoke(context);

            LogMessage log = Assert.Single(logger.GetLogMessages());
            Assert.Equal(LogLevel.Warning, log.Level);
            Assert.Equal(
                "An error occurred while checking whether the rejected request path '/admin/foo' matches a function route.",
                log.FormattedMessage);
            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_RouteProbeThrowsWithoutLogger_ReturnsNotFound()
        {
            var router = new Mock<IWebJobsRouter>();
            router
                .Setup(p => p.RouteAsync(It.IsAny<RouteContext>()))
                .ThrowsAsync(new InvalidOperationException("Route probe failed."));
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router.Object);
            var context = CreateContext("/admin/foo", registerLogger: false);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            router.VerifyAll();
            Mock.Get(context.RequestServices).Verify(
                p => p.GetService(typeof(ILogger<ReservedRouteGuardMiddleware>)),
                Times.Once);
        }

        [Fact]
        public async Task Invoke_LoggerResolutionThrows_ReturnsNotFound()
        {
            var (router, _) = CreateRouter("{*route}", "ReservedRouteCatchAll");
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router);
            var context = CreateContext(
                "/admin/foo",
                loggerResolutionException: new ObjectDisposedException(nameof(IServiceProvider)));

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            Mock.Get(context.RequestServices).Verify(
                p => p.GetService(typeof(ILogger<ReservedRouteGuardMiddleware>)),
                Times.Once);
        }

        [Fact]
        public async Task Invoke_LoggerThrows_ReturnsNotFound()
        {
            var (router, _) = CreateRouter("{*route}", "ReservedRouteCatchAll");
            var logger = new Mock<ILogger<ReservedRouteGuardMiddleware>>();
            logger
                .Setup(p => p.Log(
                    It.IsAny<LogLevel>(),
                    It.IsAny<EventId>(),
                    It.IsAny<It.IsAnyType>(),
                    It.IsAny<Exception>(),
                    It.IsAny<Func<It.IsAnyType, Exception, string>>()))
                .Throws(new InvalidOperationException("Logging failed."));
            var middleware = CreateMiddleware(_ => Task.CompletedTask, router: router);
            var context = CreateContext("/admin/foo", logger.Object);

            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status404NotFound, context.Response.StatusCode);
            logger.VerifyAll();
        }

        [Fact]
        public async Task Invoke_ReservedPath_WhenOptOutRemoved_EnforcesSubsequentRequest()
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

            Assert.Equal(1, nextInvocations);
            Assert.Equal(StatusCodes.Status404NotFound, secondContext.Response.StatusCode);
        }

        [Fact]
        public async Task Invoke_ReservedPath_WhenOptOutEnabled_AllowsSubsequentRequest()
        {
            bool nextInvoked = false;
            var reservedRouteOptions = new ReservedRouteOptions
            {
                AdminWarmupRouteEnabled = true
            };
            var middleware = CreateMiddleware(context =>
            {
                nextInvoked = true;
                context.Response.StatusCode = StatusCodes.Status202Accepted;
                return Task.CompletedTask;
            }, reservedRouteOptions: reservedRouteOptions);

            await middleware.Invoke(CreateContext("/admin/foo"));
            reservedRouteOptions.DisableReservedRouteEnforcement = true;
            var secondContext = CreateContext("/runtime/foo");
            await middleware.Invoke(secondContext);

            Assert.Equal(StatusCodes.Status202Accepted, secondContext.Response.StatusCode);
            Assert.True(nextInvoked);
        }

        [Theory]
        [InlineData(null, false, true)]
        [InlineData(ScriptConstants.DynamicSku, false, false)]
        [InlineData(null, true, false)]
        public void ReservedRouteOptionsSetup_ReturnsExpectedWarmupRouteValue(string sku, bool isLinuxConsumption, bool expected)
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, sku);
            if (isLinuxConsumption)
            {
                environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            }

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
            ReservedRouteOptions reservedRouteOptions = null,
            IWebJobsRouter router = null,
            IScriptHostManager scriptHostManager = null)
        {
            return new ReservedRouteGuardMiddleware(
                next,
                new TestOptionsMonitor<StandbyOptions>(standbyOptions),
                new TestOptionsMonitor<ReservedRouteOptions>(
                    reservedRouteOptions ?? new ReservedRouteOptions { AdminWarmupRouteEnabled = true }),
                router ?? new WebJobsRouter(Mock.Of<IInlineConstraintResolver>()),
                scriptHostManager ?? Mock.Of<IScriptHostManager>(p => p.State == ScriptHostState.Running));
        }

        private static (IWebJobsRouter Router, Mock<IWebJobsRouteHandler> Handler) CreateRouter(
            string route,
            string functionName,
            params string[] methods)
        {
            var handler = new Mock<IWebJobsRouteHandler>(MockBehavior.Strict);
            var router = new WebJobsRouter(Mock.Of<IInlineConstraintResolver>());
            WebJobsRouteBuilder builder = router.CreateBuilder(handler.Object, routePrefix: string.Empty);
            var constraints = new RouteValueDictionary();
            if (methods.Length > 0)
            {
                constraints.Add("httpMethod", new HttpMethodRouteConstraint(methods));
            }

            builder.MapFunctionRoute(functionName, route, constraints, functionName);
            router.AddFunctionRoutes(builder.Build(), null);

            return (router, handler);
        }

        private static DefaultHttpContext CreateContext(
            string path,
            ILogger<ReservedRouteGuardMiddleware> logger = null,
            bool registerLogger = true,
            Exception loggerResolutionException = null)
        {
            var services = new Mock<IServiceProvider>();
            if (loggerResolutionException is not null)
            {
                services
                    .Setup(p => p.GetService(typeof(ILogger<ReservedRouteGuardMiddleware>)))
                    .Throws(loggerResolutionException);
            }
            else if (registerLogger)
            {
                services
                    .Setup(p => p.GetService(typeof(ILogger<ReservedRouteGuardMiddleware>)))
                    .Returns(logger ?? Mock.Of<ILogger<ReservedRouteGuardMiddleware>>());
            }

            services
                .Setup(p => p.GetService(typeof(ILoggerFactory)))
                .Returns(NullLoggerFactory.Instance);

            return new DefaultHttpContext
            {
                RequestServices = services.Object,
                Request =
                {
                    Method = HttpMethods.Get,
                    Path = path
                },
                Response =
                {
                    Body = new MemoryStream()
                }
            };
        }
    }
}
