// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.WebJobs.Script.Config;
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
            HostSettings = new WebHostSettings
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
                        [EnvironmentSettingNames.AppInsightsInstrumentationKey] = TestChannelLoggerProviderFactory.ApplicationInsightsKey
                    });
                })
                .ConfigureServices(services =>
                {
                    services.Replace(new ServiceDescriptor(typeof(WebHostSettings), HostSettings));
                    services.Replace(new ServiceDescriptor(typeof(ILoggerProviderFactory), new TestChannelLoggerProviderFactory(Channel)));
                    services.Replace(new ServiceDescriptor(typeof(ISecretManager), new TestSecretManager()));
                });

            _testServer = new TestServer(hostBuilder);

            var scriptConfig = _testServer.Host.Services.GetService<WebHostResolver>().GetScriptHostConfiguration(HostSettings);

            InitializeConfig(scriptConfig);

            HttpClient = _testServer.CreateClient();
            HttpClient.BaseAddress = new Uri("https://localhost/");

            TestHelpers.WaitForWebHost(HttpClient);
        }

        public ScriptHost GetScriptHost()
        {
            return _testServer.Host.Services.GetService<WebHostResolver>().GetWebScriptHostManager(HostSettings).Instance;
        }

        public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        public WebHostSettings HostSettings { get; private set; }

        public HttpClient HttpClient { get; private set; }

        protected void InitializeConfig(ScriptHostConfiguration config)
        {
            config.OnConfigurationApplied = c =>
            {
                // turn this off as it makes validation tough
                config.HostConfig.Aggregator.IsEnabled = false;

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