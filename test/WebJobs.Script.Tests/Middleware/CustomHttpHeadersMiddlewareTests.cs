// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class CustomHttpHeadersMiddlewareTests
    {
        private bool _nextInvoked = false;
        private IWebHost _host;

        [Fact]
        public async Task Invoke_hasCustomHeaders_AddsResponseHeaders()
        {
            var headers = new CustomHttpHeadersOptions
            {
                { "X-Content-Type-Options", "nosniff" },
                { "Feature-Policy", "camera 'none'; geolocation 'none'" }
            };
            var headerOptions = new OptionsWrapper<CustomHttpHeadersOptions>(headers);

            using (var host = GetTestHost(o =>
            {
                o.Add("X-Content-Type-Options", "nosniff");
                o.Add("Feature-Policy", "camera 'none'; geolocation 'none'");
            }))
            {
                await host.StartAsync();
                HttpResponseMessage response = await host.GetTestClient().GetAsync(string.Empty);
                await host.StopAsync();

                Assert.True(_nextInvoked);
                Assert.Equal(response.Headers.GetValues("X-Content-Type-Options").Single(), "nosniff");
                Assert.Equal(response.Headers.GetValues("Feature-Policy").Single(), "camera 'none'; geolocation 'none'");
            }
        }

        [Fact]
        public async Task Invoke_noCustomHeaders_DoesNotAddResponseHeader()
        {
            using (var host = GetTestHost())
            {
                await _host.StartAsync();
                HttpResponseMessage response = await host.GetTestClient().GetAsync(string.Empty);
                await _host.StopAsync();

                Assert.True(_nextInvoked);
                Assert.True(response.Headers.Count() == 0,
                    $"Expected 0 headers. Actual: {string.Join(", ", response.Headers.Select(h => h.Key))}");
            }
        }

        private IWebHost GetTestHost(Action<CustomHttpHeadersOptions> configureOptions = null)
        {
            // The custom middleware relies on the host starting the request (thus invoking OnStarting),
            // so we need to create a test host to flow through the entire pipeline.
            _host = new WebHostBuilder()
                .UseTestServer()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    s.AddSingleton<IJobHostHttpMiddleware, CustomHttpHeadersMiddleware>();
                    s.AddOptions<CustomHttpHeadersOptions>().Configure(o => configureOptions?.Invoke(o));
                })
                .Configure(app =>
                {
                    app.UseMiddleware<JobHostPipelineMiddleware>();
                    app.Run((context) =>
                    {
                        _nextInvoked = true;
                        context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                        return Task.CompletedTask;
                    });
                })
                .Build();

            return _host;
        }
    }
}
