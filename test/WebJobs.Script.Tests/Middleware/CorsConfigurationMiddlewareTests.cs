// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Middleware
{
    public class CorsConfigurationMiddlewareTests
    {
        [Fact]
        public async Task Invoke_HasCorsConfig_InvokesNext()
        {
            var testOrigin = "https://functions.azure.com";
            var hostCorsOptions = new OptionsWrapper<HostCorsOptions>(new HostCorsOptions
            {
                AllowedOrigins = new List<string> { testOrigin }
            });
            var testPolicy = new CorsPolicy();
            var testOptions = new CorsOptions();
            testOptions.AddDefaultPolicy(testPolicy);
            var corsOptions = new OptionsWrapper<CorsOptions>(testOptions);
            var corsFactory = new CorsMiddlewareFactory(corsOptions, NullLoggerFactory.Instance);

            bool nextInvoked = false;
            RequestDelegate next = (context) =>
            {
                nextInvoked = true;
                context.Response.StatusCode = (int)HttpStatusCode.Accepted;
                return Task.CompletedTask;
            };

            var middleware = new JobHostCorsMiddleware(hostCorsOptions, corsFactory);

            var httpContext = new DefaultHttpContext();
            await middleware.Invoke(httpContext, next);
            Assert.True(nextInvoked);
        }

        [Fact]
        public async Task Invoke_OriginAllowed_AddsExpectedHeaders()
        {
            var envars = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ContainerName, "foo" },
            };
            var testEnv = new TestEnvironment(envars);
            var testOrigin = "https://functions.azure.com";
            var hostCorsOptions = new HostCorsOptions
            {
                AllowedOrigins = new List<string> { testOrigin },
                SupportCredentials = true,
            };

            var builder = new WebHostBuilder()
                .Configure(app =>
                {
                    app.UseMiddleware<JobHostPipelineMiddleware>();
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("Hello world");
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddTransient<IEnvironment>(factory => testEnv);
                    services.ConfigureOptions<CorsOptionsSetup>();
                    services.AddTransient<IConfigureOptions<HostCorsOptions>>(factory => new TestHostCorsOptionsSetup(hostCorsOptions));
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.AddCors();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostCorsMiddleware>());
                    services.AddSingleton<ICorsMiddlewareFactory, CorsMiddlewareFactory>();
                });

            var server = new TestServer(builder);

            var client = server.CreateClient();
            client.DefaultRequestHeaders.Add("Origin", testOrigin);

            var response = await client.GetAsync(string.Empty);
            response.EnsureSuccessStatusCode();
            Assert.Equal("Hello world", await response.Content.ReadAsStringAsync());

            IEnumerable<string> originHeaderValues;
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out originHeaderValues));
            Assert.Equal(testOrigin, originHeaderValues.FirstOrDefault());

            IEnumerable<string> allowCredsHeaderValues;
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out allowCredsHeaderValues));
            Assert.Equal("true", allowCredsHeaderValues.FirstOrDefault());
        }

        [Fact]
        public async Task Invoke_OriginNotAllowed_DoesNotAddHeaders()
        {
            var envars = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ContainerName, "foo" },
            };
            var testEnv = new TestEnvironment(envars);
            var badOrigin = "http://badorigin.com";
            var hostCorsOptions = new HostCorsOptions
            {
                AllowedOrigins = new List<string> { "https://functions.azure.com" },
                SupportCredentials = true,
            };

            var builder = new WebHostBuilder()
                .Configure(app =>
                {
                    app.UseMiddleware<JobHostPipelineMiddleware>();
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("Hello world");
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddTransient<IEnvironment>(factory => testEnv);
                    services.ConfigureOptions<CorsOptionsSetup>();
                    services.AddTransient<IConfigureOptions<HostCorsOptions>>(factory => new TestHostCorsOptionsSetup(hostCorsOptions));
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.AddCors();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostCorsMiddleware>());
                    services.AddSingleton<ICorsMiddlewareFactory, CorsMiddlewareFactory>();
                });

            var server = new TestServer(builder);

            var client = server.CreateClient();
            client.DefaultRequestHeaders.Add("Origin", badOrigin);

            var response = await client.GetAsync(string.Empty);
            response.EnsureSuccessStatusCode();
            Assert.Equal("Hello world", await response.Content.ReadAsStringAsync());

            IEnumerable<string> originHeaderValues;
            Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Origin", out originHeaderValues));

            IEnumerable<string> allowCredsHeaderValues;
            Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out allowCredsHeaderValues));

            IEnumerable<string> allowMethods;
            Assert.False(response.Headers.TryGetValues("Access-Control-Allow-Methods", out allowMethods));
        }

        [Fact]
        public async Task Invoke_Adds_AccessControlAllowMethods()
        {
            var envars = new Dictionary<string, string>()
            {
                { EnvironmentSettingNames.ContainerName, "foo" },
            };
            var testEnv = new TestEnvironment(envars);
            var testOrigin = "https://functions.azure.com";
            var hostCorsOptions = new HostCorsOptions
            {
                AllowedOrigins = new List<string> { testOrigin },
                SupportCredentials = true,
            };

            var builder = new WebHostBuilder()
                .Configure(app =>
                {
                    app.UseMiddleware<JobHostPipelineMiddleware>();
                    app.Run(async context =>
                    {
                        await context.Response.WriteAsync("Hello world");
                    });
                })
                .ConfigureServices(services =>
                {
                    services.AddTransient<IEnvironment>(factory => testEnv);
                    services.ConfigureOptions<CorsOptionsSetup>();
                    services.AddTransient<IConfigureOptions<HostCorsOptions>>(factory => new TestHostCorsOptionsSetup(hostCorsOptions));
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.AddCors();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostCorsMiddleware>());
                    services.AddSingleton<ICorsMiddlewareFactory, CorsMiddlewareFactory>();
                });

            var server = new TestServer(builder);

            var client = server.CreateClient();
            client.DefaultRequestHeaders.Add("Origin", testOrigin);
            client.DefaultRequestHeaders.Add("Access-Control-Request-Method", HttpMethod.Post.ToString());

            var request = new HttpRequestMessage(HttpMethod.Options, string.Empty);

            var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            IEnumerable<string> originHeaderValues;
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out originHeaderValues));
            Assert.Equal(testOrigin, originHeaderValues.FirstOrDefault());

            IEnumerable<string> allowCredsHeaderValues;
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Credentials", out allowCredsHeaderValues));
            Assert.Equal("true", allowCredsHeaderValues.FirstOrDefault());

            IEnumerable<string> allowMethods;
            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Methods", out allowMethods));
            Assert.Equal("POST", allowMethods.FirstOrDefault());
        }

        public class TestHostCorsOptionsSetup : IConfigureOptions<HostCorsOptions>
        {
            private readonly HostCorsOptions _options;

            public TestHostCorsOptionsSetup(HostCorsOptions options)
            {
                _options = options;
            }

            public void Configure(HostCorsOptions options)
            {
                options.AllowedOrigins = _options.AllowedOrigins;
                options.SupportCredentials = _options.SupportCredentials;
            }
        }
    }
}
