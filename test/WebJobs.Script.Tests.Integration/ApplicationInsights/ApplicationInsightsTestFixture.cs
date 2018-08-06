// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : IDisposable
    {
        private readonly TestServer _testServer;

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
        {
            WebHostOptions = new ScriptApplicationHostOptions
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, scriptRoot),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Environment.CurrentDirectory // not used
            };

            var hostBuilder = Program.CreateWebHostBuilder()
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        [EnvironmentSettingNames.AppInsightsInstrumentationKey] = ""//TestChannelLoggerProviderFactory.ApplicationInsightsKey - TODO: review (brettsam)
                    });
                })
                .ConfigureServices(services =>
                {
                    services.Replace(new ServiceDescriptor(typeof(IOptions<ScriptApplicationHostOptions>), new OptionsWrapper<ScriptApplicationHostOptions>(WebHostOptions)));
                    //services.Replace(new ServiceDescriptor(typeof(ILoggerProviderFactory), new TestChannelLoggerProviderFactory(Channel)));
                    services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
                    services.AddSingleton<IScriptHostBuilder, ScriptHostBuilder>();
                });

            _testServer = new TestServer(hostBuilder);

            HttpClient = _testServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(HttpClient);
        }

        // TODO: DI (FACAVAL) brettsam - missing type?
        // public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        public ScriptApplicationHostOptions WebHostOptions { get; private set; }

        public HttpClient HttpClient { get; private set; }

        public void Dispose()
        {
            _testServer?.Dispose();
            HttpClient?.Dispose();
        }

        private class ScriptHostBuilder : IScriptHostBuilder
        {
            public void Configure(IHostBuilder builder)
            {
                builder.ConfigureServices(s =>
                {
                    s.Configure<ScriptJobHostOptions>(o =>
                    {
                        o.Functions = new[] { "Scenarios", "HttpTrigger-Scenarios" };
                    });
                });
            }
        }
    }
}