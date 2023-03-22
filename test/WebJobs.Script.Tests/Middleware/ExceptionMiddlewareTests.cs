// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
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
        private readonly TestLoggerProvider _loggerProvider = new TestLoggerProvider();

        [Fact]
        public async Task Invoke_HandlesHttpExceptions()
        {
            var ex = new HttpException(StatusCodes.Status502BadGateway);

            using (var server = GetTestServer(_ => throw ex))
            {
                var client = server.CreateClient();
                HttpResponseMessage response = await client.GetAsync(string.Empty);

                Assert.Equal(ex.StatusCode, (int)response.StatusCode);

                var log = _loggerProvider.GetAllLogMessages().Single(p => p.Category.Contains(nameof(ExceptionMiddleware)));
                Assert.Equal("An unhandled host error has occurred.", log.FormattedMessage);
                Assert.Same(ex, log.Exception);
            }
        }

        [Fact]
        public async Task Invoke_HandlesNonHttpExceptions()
        {
            var ex = new Exception("Kaboom!");

            using (var server = GetTestServer(_ => throw ex))
            {
                var client = server.CreateClient();
                HttpResponseMessage response = await client.GetAsync(string.Empty);

                Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);

                var log = _loggerProvider.GetAllLogMessages().Single(p => p.Category.Contains(nameof(ExceptionMiddleware)));
                Assert.Equal("An unhandled host error has occurred.", log.FormattedMessage);
                Assert.Same(ex, log.Exception);
            }
        }

        [Fact]
        public async Task Invoke_HandlesFunctionInvocationExceptions()
        {
            var ex = new FunctionInvocationException("Kaboom!");

            using (var server = GetTestServer(_ => throw ex))
            {
                var client = server.CreateClient();
                HttpResponseMessage response = await client.GetAsync(string.Empty);

                Assert.Equal(StatusCodes.Status500InternalServerError, (int)response.StatusCode);
                Assert.Null(_loggerProvider.GetAllLogMessages().SingleOrDefault(p => p.Category.Contains(nameof(ExceptionMiddleware))));
            }
        }

        [Fact]
        public async Task Invoke_LogsError_AfterResponseWritten()
        {
            var ex = new InvalidOperationException("Kaboom!");

            async Task WriteThenThrow(HttpContext context)
            {
                await context.Response.WriteAsync("Hi.");
                throw ex;
            }

            using (var server = GetTestServer(c => WriteThenThrow(c)))
            {
                var client = server.CreateClient();
                HttpResponseMessage response = await client.GetAsync(string.Empty);

                // Because the response had already been written, this cannot change.
                Assert.Equal(StatusCodes.Status200OK, (int)response.StatusCode);
                Assert.Equal("Hi.", await response.Content.ReadAsStringAsync());

                var logs = _loggerProvider.GetAllLogMessages().Where(p => p.Category.Contains(nameof(ExceptionMiddleware)));
                Assert.Collection(logs,
                    m =>
                    {
                        Assert.Equal("An unhandled host error has occurred.", m.FormattedMessage);
                        Assert.Same(ex, m.Exception);
                        Assert.Equal("UnhandledHostError", m.EventId.Name);
                    },
                    m =>
                    {
                        Assert.Equal("The response has already started, the status code will not be modified.", m.FormattedMessage);
                        Assert.Equal("ResponseStarted", m.EventId.Name);
                    });
            }
        }

        private TestServer GetTestServer(Func<HttpContext, Task> callback)
        {
            // The custom middleware relies on the host starting the request (thus invoking OnStarting),
            // so we need to create a test host to flow through the entire pipeline.
            var builder = new WebHostBuilder()
                .ConfigureLogging(b =>
                {
                    b.AddProvider(_loggerProvider);
                    b.SetMinimumLevel(LogLevel.Debug);
                })
                .Configure(app =>
                {
                    app.Use(async (httpContext, next) =>
                    {
                        try
                        {
                            await next();
                        }
                        catch (InvalidOperationException)
                        {
                            // The TestServer cannot handle exceptions after the
                            // host has started.
                        }
                    });

                    app.UseMiddleware<ExceptionMiddleware>();

                    app.Run((context) =>
                    {
                        return callback(context);
                    });
                });

            return new TestServer(builder);
        }
    }
}
