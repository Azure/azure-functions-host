// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.WebJobs.Script.Tests;

namespace Microsoft.Azure.WebJobs.Script.Tests.ApplicationInsights
{
    public abstract class ApplicationInsightsTestFixture : EndToEndTestFixture
    {
        static ApplicationInsightsTestFixture()
        {
            // We need to set this to something in order to trigger App Insights integration. But since
            // we're hitting a local HttpListener, it can be anything.
            ScriptSettingsManager.Instance.ApplicationInsightsInstrumentationKey = TestChannelLoggerFactoryBuilder.ApplicationInsightsKey;
        }

        public ApplicationInsightsTestFixture(string scriptRoot, string testId)
            : base(scriptRoot, testId)
        {
        }

        public TestTelemetryChannel Channel { get; private set; } = new TestTelemetryChannel();

        protected override void InitializeConfig(ScriptHostConfiguration config)
        {
            var builder = new TestChannelLoggerFactoryBuilder(Channel);
            config.HostConfig.AddService<ILoggerFactoryBuilder>(builder);

            // turn this off as it makes validation tough
            config.HostConfig.Aggregator.IsEnabled = false;

            config.OnConfigurationApplied = c =>
            {
                // Overwrite the generated function whitelist to only include one function.
                c.Functions = new[] { "Scenarios" };
            };
        }
    }
}
