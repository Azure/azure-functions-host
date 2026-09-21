// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Handlers
{
    public class SystemTraceMiddlewareTests
    {
        private const string RouteTemplate = "api/products/{category}/{id}";

        private readonly TestLoggerProvider _loggerProvider;
        private readonly ILoggerFactory _loggerFactory;
        private readonly SystemTraceMiddleware _middleware;

        public SystemTraceMiddlewareTests()
        {
            _loggerProvider = new TestLoggerProvider();
            _loggerFactory = new LoggerFactory();
            _loggerFactory.AddProvider(_loggerProvider);

            _middleware = CreateMiddleware(async context => await Task.Delay(25));
        }

        [Fact]
        public async Task SendAsync_WritesExpectedTraces()
        {
            string requestId = Guid.NewGuid().ToString();
            var context = CreateContext(requestId, "http://functions.com/api/testfunc?code=123");

            await _middleware.Invoke(context);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(2, logs.Length);

            var executing = ParseDetails(logs[0], "Executing HTTP request");
            Assert.Equal(2, executing.Count);
            Assert.Equal(requestId, executing["requestId"]);
            Assert.Equal("GET", executing["method"]);

            var executed = ParseDetails(logs[1], "Executed HTTP request");
            Assert.Equal(5, executed.Count);
            Assert.Equal(requestId, executed["requestId"]);
            Assert.Equal(200, executed["status"]);
            Assert.True((long)executed["duration"] > 0);
            Assert.Equal($"({AuthLevelAuthenticationDefaults.AuthenticationScheme}:Function, CustomScheme:CustomLevel)", (string)executed["identities"]);

            // the request was never routed, so there is no template to report
            Assert.Equal(string.Empty, (string)executed["route"]);
        }

        [Fact]
        public async Task SendAsync_WritesRouteTemplate_WhenRequestWasRouted()
        {
            // routing runs downstream of this middleware, so the feature only exists once the pipeline unwinds
            var middleware = CreateMiddleware(context =>
            {
                SetRoutingFeature(context, RouteTemplate);
                return Task.CompletedTask;
            });

            string requestId = Guid.NewGuid().ToString();
            var context = CreateContext(requestId, "http://functions.com/api/products/electronics/12345");

            await middleware.Invoke(context);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(2, logs.Length);

            var executing = ParseDetails(logs[0], "Executing HTTP request");
            Assert.Equal(2, executing.Count);

            var executed = ParseDetails(logs[1], "Executed HTTP request");
            Assert.Equal(RouteTemplate, (string)executed["route"]);
        }

        [Fact]
        public async Task SendAsync_DoesNotLogCallerSuppliedValues()
        {
            var middleware = CreateMiddleware(context =>
            {
                SetRoutingFeature(context, "api/GetLearnerProfile/{email}");
                return Task.CompletedTask;
            });

            var context = CreateContext(Guid.NewGuid().ToString(), "http://functions.com/api/GetLearnerProfile/someone@contoso.com");

            await middleware.Invoke(context);

            foreach (var log in _loggerProvider.GetAllLogMessages())
            {
                Assert.DoesNotContain("someone@contoso.com", log.FormattedMessage, StringComparison.Ordinal);
                Assert.DoesNotContain("TestAgent", log.FormattedMessage, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void GetRouteTemplate_ReturnsEmpty_WhenRequestWasNotRouted()
        {
            Assert.Equal(string.Empty, SystemTraceMiddleware.GetRouteTemplate(new DefaultHttpContext()));
        }

        [Fact]
        public void GetRouteTemplate_ReturnsEmpty_WhenRouteDataIsNull()
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IRoutingFeature>(new RoutingFeature());

            Assert.Equal(string.Empty, SystemTraceMiddleware.GetRouteTemplate(context));
        }

        [Fact]
        public void GetRouteTemplate_ReturnsEmpty_WhenNoRouteWasMatched()
        {
            var context = new DefaultHttpContext();
            context.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = new RouteData() });

            Assert.Equal(string.Empty, SystemTraceMiddleware.GetRouteTemplate(context));
        }

        [Fact]
        public void GetRouteTemplate_ReturnsMatchedTemplate()
        {
            var context = new DefaultHttpContext();
            SetRoutingFeature(context, RouteTemplate);

            Assert.Equal(RouteTemplate, SystemTraceMiddleware.GetRouteTemplate(context));
        }

        private static void SetRoutingFeature(HttpContext context, string routeTemplate)
        {
            var route = new Route(
                target: new RouteHandler(_ => Task.CompletedTask),
                routeName: "test",
                routeTemplate: routeTemplate,
                defaults: null,
                constraints: null,
                dataTokens: null,
                inlineConstraintResolver: new TestInlineConstraintResolver());

            var routeData = new RouteData();
            routeData.Routers.Add(route);

            context.Features.Set<IRoutingFeature>(new RoutingFeature { RouteData = routeData });
        }

        private static DefaultHttpContext CreateContext(string requestId, string uriString)
        {
            var context = new DefaultHttpContext();
            Uri uri = new Uri(uriString);
            var requestFeature = context.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            var headers = new HeaderDictionary();
            headers.Add(ScriptConstants.AntaresLogIdHeaderName, new StringValues(requestId));
            headers.Add("User-Agent", new StringValues("TestAgent"));
            requestFeature.Headers = headers;

            var principal = new ClaimsPrincipal();
            principal.AddIdentity(new ClaimsIdentity(new List<Claim>
            {
                new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Function.ToString())
            }, AuthLevelAuthenticationDefaults.AuthenticationScheme));
            principal.AddIdentity(new ClaimsIdentity(new List<Claim>
            {
                new Claim(SecurityConstants.AuthLevelClaimType, "CustomLevel")
            }, "CustomScheme"));
            context.User = principal;

            return context;
        }

        private static JObject ParseDetails(LogMessage log, string expectedMessage)
        {
            Assert.Equal(typeof(SystemTraceMiddleware).FullName, log.Category);
            Assert.Equal(LogLevel.Information, log.Level);

            var idx = log.FormattedMessage.IndexOf(':');
            Assert.Equal(expectedMessage, log.FormattedMessage.Substring(0, idx).Trim());

            return JObject.Parse(log.FormattedMessage.Substring(idx + 1).Trim());
        }

        private SystemTraceMiddleware CreateMiddleware(RequestDelegate next)
        {
            return new SystemTraceMiddleware(next, _loggerFactory.CreateLogger<SystemTraceMiddleware>());
        }

        private sealed class TestInlineConstraintResolver : IInlineConstraintResolver
        {
            public IRouteConstraint ResolveConstraint(string inlineConstraint) => null;
        }
    }
}
