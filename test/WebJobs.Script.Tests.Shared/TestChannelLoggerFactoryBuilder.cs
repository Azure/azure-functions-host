// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestChannelLoggerFactoryBuilder : DefaultLoggerFactoryBuilder
    {
        public const string ApplicationInsightsKey = "some_key";

        private readonly TestTelemetryChannel _channel;

        public TestChannelLoggerFactoryBuilder(TestTelemetryChannel channel)
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

    internal class TestTelemetryClientFactory : ScriptTelemetryClientFactory
    {
        private TestTelemetryChannel _channel;

        public TestTelemetryClientFactory(Func<string, LogLevel, bool> filter, TestTelemetryChannel channel)
            : base(TestChannelLoggerFactoryBuilder.ApplicationInsightsKey, filter)
        {
            _channel = channel;
        }

        protected override ITelemetryChannel CreateTelemetryChannel()
        {
            return _channel;
        }
    }
}
