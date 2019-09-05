// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class CustomHttpHeadersMiddlewareTests
    {
        private bool _nextInvoked = false;

        [Fact]
        public async Task Invoke_hasCustomHeaders_AddsResponseHeaders()
        {
            var headers = new CustomHttpHeadersOptions
            {
                { "X-Content-Type-Options", "nosniff" },
                { "Feature-Policy", "camera 'none'; geolocation 'none'" }
            };
            var headerOptions = new OptionsWrapper<CustomHttpHeadersOptions>(headers);

            HttpClient client = GetTestHttpClient(o =>
            {
                o.Add("X-Content-Type-Options", "nosniff");
                o.Add("Feature-Policy", "camera 'none'; geolocation 'none'");
            });
            HttpResponseMessage response = await client.GetAsync(string.Empty);

            Assert.True(_nextInvoked);
            Assert.Equal(response.Headers.GetValues("X-Content-Type-Options").Single(), "nosniff");
            Assert.Equal(response.Headers.GetValues("Feature-Policy").Single(), "camera 'none'; geolocation 'none'");
        }

        [Fact]
        public async Task Invoke_noCustomHeaders_DoesNotAddResponseHeader()
        {
            HttpResponseMessage response = await GetTestHttpClient().GetAsync(string.Empty);
            Assert.True(_nextInvoked);
            Assert.Equal(response.Headers.Count(), 0);
        }

        private HttpClient GetTestHttpClient(Action<CustomHttpHeadersOptions> configureOptions = null)
        {
            // The custom middleware relies on the host starting the request (thus invoking OnStarting),
            // so we need to create a test host to flow through the entire pipeline.
            var host = new WebHostBuilder()
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
                    app.Use((context, next) =>
                    {
                        _nextInvoked = true;
                        context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                        return Task.CompletedTask;
                    });
                })
                .Build();

            host.Start();
            return host.GetTestClient();
        }
    }
}
