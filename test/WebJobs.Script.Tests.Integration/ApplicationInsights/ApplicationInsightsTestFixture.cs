// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : IDisposable
    {
        private readonly TestServer _testServer;

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
        {
            WebHostOptions = new ScriptWebHostOptions
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
                    services.Replace(new ServiceDescriptor(typeof(ScriptWebHostOptions), WebHostOptions));
                    //services.Replace(new ServiceDescriptor(typeof(ILoggerProviderFactory), new TestChannelLoggerProviderFactory(Channel)));
                    services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
                });

            _testServer = new TestServer(hostBuilder);

            var scriptConfig = _testServer.Host.Services.GetService<WebHostResolver>().GetScriptHostConfiguration(WebHostOptions);

            InitializeConfig(scriptConfig);

            HttpClient = _testServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public ScriptHost GetScriptHost()
        {
            return _testServer.Host.Services.GetService<WebHostResolver>().GetWebScriptHostManager(WebHostOptions).Instance;
        }

        // TODO: DI (FACAVAL) brettsam - missing type?
        // public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        public ScriptWebHostOptions WebHostOptions { get; private set; }

        public HttpClient HttpClient { get; private set; }

        protected void InitializeConfig(ScriptHostOptions options)
        {
            options.OnConfigurationApplied = c =>
            {
                // turn this off as it makes validation tough
                // TODO: DI (FACAVAL) Review- brettsam
                //options.HostConfig.Aggregator.IsEnabled = false;

                // Overwrite the generated function whitelist to only include two functions.
                c.Functions = new[] { "Scenarios", "HttpTrigger-Scenarios" };
            };
        }

        public void Dispose()
        {
            _testServer?.Dispose();
            HttpClient?.Dispose();
        }
    }
}