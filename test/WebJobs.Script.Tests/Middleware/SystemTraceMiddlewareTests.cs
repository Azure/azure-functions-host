// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
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
        private readonly TestLoggerProvider _loggerProvider;
        private readonly SystemTraceMiddleware _middleware;

        public SystemTraceMiddlewareTests()
        {
            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            RequestDelegate requestDelegate = async (HttpContext context) =>
            {
                await Task.Delay(25);
            };

            var logger = loggerFactory.CreateLogger<SystemTraceMiddleware>();
            _middleware = new SystemTraceMiddleware(requestDelegate, logger);
        }

        [Fact]
        public async Task SendAsync_WritesExpectedTraces()
        {
            string requestId = Guid.NewGuid().ToString();
            var context = new DefaultHttpContext();
            Uri uri = new Uri("http://functions.com/api/testfunc?code=123");
            var requestFeature = context.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            var headers = new HeaderDictionary();
            headers.Add(ScriptConstants.AntaresLogIdHeaderName, new StringValues(requestId));
            requestFeature.Headers = headers;

            var claims = new List<Claim>
            {
                new Claim(SecurityConstants.AuthLevelClaimType, AuthorizationLevel.Function.ToString())
            };
            var identity = new ClaimsIdentity(claims, AuthLevelAuthenticationDefaults.AuthenticationScheme);
            context.User = new ClaimsPrincipal(identity);

            await _middleware.Invoke(context);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(2, logs.Length);

            // validate executing trace
            var log = logs[0];
            Assert.Equal(typeof(SystemTraceMiddleware).FullName, log.Category);
            Assert.Equal(LogLevel.Information, log.Level);
            var idx = log.FormattedMessage.IndexOf(':');
            var message = log.FormattedMessage.Substring(0, idx).Trim();
            Assert.Equal("Executing HTTP request", message);
            var details = log.FormattedMessage.Substring(idx + 1).Trim();
            var jo = JObject.Parse(details);
            Assert.Equal(3, jo.Count);
            Assert.Equal(requestId, jo["requestId"]);
            Assert.Equal("GET", jo["method"]);
            Assert.Equal("/api/testfunc", jo["uri"]);

            // validate executed trace
            log = logs[1];
            Assert.Equal(typeof(SystemTraceMiddleware).FullName, log.Category);
            Assert.Equal(LogLevel.Information, log.Level);
            idx = log.FormattedMessage.IndexOf(':');
            message = log.FormattedMessage.Substring(0, idx).Trim();
            Assert.Equal("Executed HTTP request", message);
            details = log.FormattedMessage.Substring(idx + 1).Trim();
            jo = JObject.Parse(details);
            Assert.Equal(6, jo.Count);
            Assert.Equal(requestId, jo["requestId"]);
            Assert.Equal("GET", jo["method"]);
            Assert.Equal("/api/testfunc", jo["uri"]);
            Assert.Equal(200, jo["status"]);
            var duration = (long)jo["duration"];
            Assert.True(duration > 0);

            var authentication = (JArray)jo["identities"];
            Assert.Equal(1, authentication.Count);
            var keyIdentity = authentication.Single();
            Assert.Equal(AuthLevelAuthenticationDefaults.AuthenticationScheme, keyIdentity["type"]);
            Assert.Equal("Function", keyIdentity["level"]);
        }
    }
}