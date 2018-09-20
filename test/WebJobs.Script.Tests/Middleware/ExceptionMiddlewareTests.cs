// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.WebJobs.Script.Tests;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class ExceptionMiddlewareTests
    {
        private readonly ILogger<ExceptionMiddleware> _logger;
        private readonly TestLoggerProvider _loggerProvider;

        public ExceptionMiddlewareTests()
        {
            _loggerProvider = new TestLoggerProvider();
            IHost host = new HostBuilder()
                .ConfigureDefaultTestWebScriptHost()
                .ConfigureLogging(l =>
                {
                    l.AddProvider(_loggerProvider);
                })
                .Build();

            _logger = host.Services.GetService<ILogger<ExceptionMiddleware>>();
        }

        [Fact]
        public async Task Invoke_HandlesHttpExceptions()
        {
            var ex = new HttpException(StatusCodes.Status502BadGateway);
            RequestDelegate next = (ctxt) =>
            {
                throw ex;
            };
            var middleware = new ExceptionMiddleware(next, _logger);

            var context = new DefaultHttpContext();
            await middleware.Invoke(context);

            Assert.Equal(ex.StatusCode, context.Response.StatusCode);

            var log = _loggerProvider.GetAllLogMessages().Single();
            Assert.Equal("An unhandled host error has occurred.", log.FormattedMessage);
            Assert.Same(ex, log.Exception);
        }

        [Fact]
        public async Task Invoke_HandlesNonHttpExceptions()
        {
            var ex = new Exception("Kaboom!");
            RequestDelegate next = (ctxt) =>
            {
                throw ex;
            };
            var middleware = new ExceptionMiddleware(next, _logger);

            var context = new DefaultHttpContext();
            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);

            var log = _loggerProvider.GetAllLogMessages().Single();
            Assert.Equal("An unhandled host error has occurred.", log.FormattedMessage);
            Assert.Same(ex, log.Exception);
        }

        [Fact]
        public async Task Invoke_HandlesFunctionInvocationExceptions()
        {
            var ex = new FunctionInvocationException("Kaboom!");
            RequestDelegate next = (ctxt) =>
            {
                throw ex;
            };
            var middleware = new ExceptionMiddleware(next, _logger);

            var context = new DefaultHttpContext();
            await middleware.Invoke(context);

            Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
            Assert.Empty(_loggerProvider.GetAllLogMessages());
        }
    }
}
