// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.WindowsServer.TelemetryChannel.Implementation;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Logging;

namespace Microsoft.WebJobs.Script.Tests
{
    public class TestChannelLoggerProviderFactory : DefaultLoggerProviderFactory
    {
        public const string ApplicationInsightsKey = "some_key";

        private readonly TestTelemetryChannel _channel;

        public TestChannelLoggerProviderFactory(TestTelemetryChannel channel)
        {
            _channel = channel;
        }

        public override IEnumerable<ILoggerProvider> CreateLoggerProviders(string hostInstanceId, ScriptHostConfiguration scriptConfig, ScriptSettingsManager settingsManager, Func<bool> fileLoggingEnabled, Func<bool> isPrimary)
        {
            // Replace TelemetryClient
            var clientFactory = new TestTelemetryClientFactory(scriptConfig.LogFilter.Filter, _channel);
            scriptConfig.HostConfig.AddService<ITelemetryClientFactory>(clientFactory);

            return base.CreateLoggerProviders(hostInstanceId, scriptConfig, settingsManager, fileLoggingEnabled, isPrimary);
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
            : base(TestChannelLoggerProviderFactory.ApplicationInsightsKey, new SamplingPercentageEstimatorSettings(), filter)
        {
            _channel = channel;
        }

        protected override ITelemetryChannel CreateTelemetryChannel()
        {
            return _channel;
        }
    }
}
