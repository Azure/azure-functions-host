// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class ScaleMonitorOptionsSetupTests
    {
        [Theory]
        [InlineData(true, true)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void ScaleOptionsSetup_Works_As_Expected(bool runtimeScaleMonitoringEnabled, bool targetBaseScalingEnabled)
        {
            var testEnvironment = new TestEnvironment();
            var testMetricLogger = new TestMetricsLogger();
            var configurationBuilder = new ConfigurationBuilder()
                .Add(new ScriptEnvironmentVariablesConfigurationSource());
            var configuration = configurationBuilder.Build();
            var testProfileManager = new Mock<IWorkerProfileManager>();

            if (runtimeScaleMonitoringEnabled)
            {
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, "1");
            }
            if (!targetBaseScalingEnabled)
            {
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.TargetBaseScalingEnabled, "0");
            }

            ScaleOptionsSetup setup = new ScaleOptionsSetup(testEnvironment);
            ScaleOptions options = new ScaleOptions();

            setup.Configure(options);

            if (runtimeScaleMonitoringEnabled)
            {
                Assert.Equal(runtimeScaleMonitoringEnabled, options.IsRuntimeScalingEnabled);
                testEnvironment.SetEnvironmentVariable(EnvironmentSettingNames.FunctionsRuntimeScaleMonitoringEnabled, "1");
            }
            if (!targetBaseScalingEnabled)
            {
                Assert.Equal(targetBaseScalingEnabled, options.IsTargetScalingEnabled);
            }
        }
    }
}
