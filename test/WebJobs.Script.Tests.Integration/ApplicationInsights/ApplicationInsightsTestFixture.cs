// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net.Http;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : IDisposable
    {
        private readonly ScriptSettingsManager _settingsManager;
        private readonly HttpConfiguration _config = new HttpConfiguration();
        private readonly HttpServer _httpServer;

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
        {
            _settingsManager = ScriptSettingsManager.Instance;

            HostSettings = new WebHostSettings
            {
                IsSelfHost = true,
                ScriptPath = Path.Combine(Environment.CurrentDirectory, scriptRoot),
                LogPath = Path.Combine(Path.GetTempPath(), @"Functions"),
                SecretsPath = Environment.CurrentDirectory, // not used
                IsAuthDisabled = true
            };
            //  WebApiConfig.Register(_config, _settingsManager, HostSettings);

            var resolver = _config.DependencyResolver;
            var hostConfig = (resolver.GetService(typeof(WebHostResolver)) as WebHostResolver).GetScriptHostConfiguration(HostSettings);

            _settingsManager.ApplicationInsightsInstrumentationKey = TestChannelLoggerProviderFactory.ApplicationInsightsKey;

            InitializeConfig(hostConfig);

            _httpServer = new HttpServer(_config);
            HttpClient = new HttpClient(_httpServer)
            {
                BaseAddress = new Uri("https://localhost/")
            };

            TestHelpers.WaitForWebHost(HttpClient);
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
            _httpServer?.Dispose();
            HttpClient?.Dispose();
        }
    }
}