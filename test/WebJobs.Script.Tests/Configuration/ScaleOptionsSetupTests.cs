// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Extensions.Options;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScaleOptionsSetupTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Configure_SetsExpectedValues(bool tbsEnabled)
        {
            var options = new ScaleOptions();
            IOptions<FunctionsHostingConfigOptions> hostingConfigOptions = Options.Create(new FunctionsHostingConfigOptions());
            hostingConfigOptions.Value.Features[GetType().Assembly.GetName().Name] = "1";

            TestEnvironment testEnvironment = new TestEnvironment();
            if (tbsEnabled)
            {
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "1");
            }

            var setup = new ScaleOptionsSetup(testEnvironment, hostingConfigOptions);
            setup.Configure(options);

            TestTargetScaler scaler = new TestTargetScaler();
            Assert.True(options.IsTargetBasedScalingEnabled == tbsEnabled);
            Assert.True(options.IsTargetBasedScalingEnabledForTriggerFunc(scaler));
            Assert.Equal(options.ScaleMetricsMaxAge, TimeSpan.FromMinutes(2));
            Assert.Equal(options.ScaleMetricsSampleInterval, TimeSpan.FromSeconds(10));
            Assert.Equal(options.MetricsPurgeEnabled, true);
        }
    }
}
