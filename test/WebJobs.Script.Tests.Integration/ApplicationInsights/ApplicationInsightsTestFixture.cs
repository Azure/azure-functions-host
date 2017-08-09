// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.WindowsServer.Channel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : EndToEndTestFixture
    {
        private const string _mockApplicationInsightsKey = "some_key";

        static ApplicationInsightsTestFixture()
        {
            // We need to set this to something in order to trigger App Insights integration. But since
            // we're hitting a local HttpListener, it can be anything.
            ScriptSettingsManager.Instance.ApplicationInsightsInstrumentationKey = _mockApplicationInsightsKey;
        }

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
            : base(scriptRoot, testId)
        {
        }

        public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        protected override void InitializeConfig(ScriptHostConfiguration config)
        {
            var builder = new TestLoggerFactoryBuilder(Channel);
            config.HostConfig.AddService<ILoggerFactoryBuilder>(builder);

            // turn this off as it makes validation tough
            config.HostConfig.Aggregator.IsEnabled = false;

            config.OnConfigurationApplied = c =>
            {
                // Overwrite the generated function whitelist to only include one function.
                c.Functions = new[] { "Scenarios" };
            };
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
                : base(_mockApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter)
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
