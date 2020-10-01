// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.WebJobs.Script.Tests;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class HostnameFixupMiddlewareTests
    {
        private const string TestHostName = "test.azurewebsites.net";

        private readonly TestLoggerProvider _loggerProvider;
        private readonly HostnameFixupMiddleware _middleware;
        private readonly HostNameProvider _hostNameProvider;

        public HostnameFixupMiddlewareTests()
        {
            _loggerProvider = new TestLoggerProvider();
            var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(_loggerProvider);

            RequestDelegate requestDelegate = async (HttpContext context) =>
            {
                await Task.Delay(25);
            };

            var mockEnvironment = new Mock<IEnvironment>(MockBehavior.Strict);
            mockEnvironment.Setup(p => p.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteHostName)).Returns(TestHostName);

            _hostNameProvider = new HostNameProvider(mockEnvironment.Object);
            _middleware = new HostnameFixupMiddleware(requestDelegate, _hostNameProvider, loggerFactory.CreateLogger<HostnameFixupMiddleware>());
        }

        [Fact]
        public async Task SendAsync_HandlesHostnameChange()
        {
            Assert.Equal(TestHostName, _hostNameProvider.Value);

            var context = CreateHttpContext();
            var requestFeature = context.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Headers.Add(ScriptConstants.AntaresDefaultHostNameHeader, "test2.azurewebsites.net");

            await _middleware.Invoke(context);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Equal(1, logs.Length);

            // validate hostname sync trace
            var log = logs[0];
            Assert.Equal("Microsoft.Azure.WebJobs.Script.WebHost.Middleware.HostnameFixupMiddleware", log.Category);
            Assert.Equal(LogLevel.Information, log.Level);
            Assert.Equal("HostName updated from 'test.azurewebsites.net' to 'test2.azurewebsites.net'", log.FormattedMessage);

            // verify the hostname was synchronized
            Assert.Equal("test2.azurewebsites.net", _hostNameProvider.Value);
        }

        [Fact]
        public async Task SendAsync_NoHostnameChange_Noop()
        {
            Assert.Equal(TestHostName, _hostNameProvider.Value);

            var context = CreateHttpContext();

            await _middleware.Invoke(context);

            var logs = _loggerProvider.GetAllLogMessages().ToArray();
            Assert.Empty(logs);

            Assert.Equal(TestHostName, _hostNameProvider.Value);
        }

        private HttpContext CreateHttpContext()
        {
            string requestId = Guid.NewGuid().ToString();
            var context = new DefaultHttpContext();
            Uri uri = new Uri("http://functions.com");
            var requestFeature = context.Request.HttpContext.Features.Get<IHttpRequestFeature>();
            requestFeature.Method = "GET";
            requestFeature.Scheme = uri.Scheme;
            requestFeature.Path = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Path, UriFormat.Unescaped);
            requestFeature.PathBase = string.Empty;
            requestFeature.QueryString = uri.GetComponents(UriComponents.KeepDelimiter | UriComponents.Query, UriFormat.Unescaped);

            var headers = new HeaderDictionary();
            headers.Add(ScriptConstants.AntaresLogIdHeaderName, new StringValues(requestId));
            requestFeature.Headers = headers;

            return context;
        }
    }
}
