// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Net.Http;
using System.Web.Http;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Extensions.Logging;
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
                LoggerFactoryBuilder = new TestLoggerFactoryBuilder(Channel),
                IsAuthDisabled = true
            };
            WebApiConfig.Register(_config, _settingsManager, HostSettings);

            var resolver = _config.DependencyResolver;
            var hostConfig = resolver.GetService<WebHostResolver>().GetScriptHostConfiguration(HostSettings);

            // configure via AppSettings, because the test project has configured an
            // empty value in app.config which we need to override
            ConfigurationManager.AppSettings[EnvironmentSettingNames.AppInsightsInstrumentationKey] = TestChannelLoggerFactoryBuilder.ApplicationInsightsKey;

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
            ConfigurationManager.AppSettings[EnvironmentSettingNames.AppInsightsInstrumentationKey] = string.Empty;

            _httpServer?.Dispose();
            HttpClient?.Dispose();
        }

        private class TestLoggerFactoryBuilder : DefaultLoggerFactoryBuilder
        {
            private readonly TestTelemetryChannel _channel;

            public TestLoggerFactoryBuilder(TestTelemetryChannel channel)
            {
                _channel = channel;
            }

            public override void AddLoggerProviders(ILoggerFactory factory, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager)
            {
                // Replace TelemetryClient
                var clientFactory = new TestTelemetryClientFactory(scriptConfig.LogFilter.Filter, _channel);
                scriptConfig.HostConfig.AddService<ITelemetryClientFactory>(clientFactory);

                base.AddLoggerProviders(factory, scriptConfig, settingsManager);
            }
        }

        public class TestTelemetryChannel : ITelemetryChannel
        {
            public ConcurrentBag<ITelemetry> Telemetries { get; private set; } = new ConcurrentBag<ITelemetry>();

            public bool? DeveloperMode { get; set; }

            public string EndpointAddress { get; set; }

            public void Dispose()
            {
            }

            public void Flush()
            {
            }

            public void Send(ITelemetry item)
            {
                Telemetries.Add(item);
            }
        }

        private class TestTelemetryClientFactory : ScriptTelemetryClientFactory
        {
            private TestTelemetryChannel _channel;

            public TestTelemetryClientFactory(Func<string, LogLevel, bool> filter, TestTelemetryChannel channel)
                : base(TestChannelLoggerFactoryBuilder.ApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter)
            {
                _channel = channel;
            }

            protected override ITelemetryChannel CreateTelemetryChannel()
            {
                return _channel;
            }
        }
    }
}
