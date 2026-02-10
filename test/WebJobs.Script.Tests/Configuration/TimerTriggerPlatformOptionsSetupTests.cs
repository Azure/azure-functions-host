// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests.Configuration
{
    public class TimerTriggerPlatformOptionsSetupTests
    {
        [Fact]
        public void Configure_WindowsConsumption_SetsError()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            environment.Platform = OSPlatform.Windows;

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Error, options.NonCronScheduleBehavior);
        }

        [Fact]
        public void Configure_FlexConsumption_SetsError()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Error, options.NonCronScheduleBehavior);
        }

        [Fact]
        public void Configure_LinuxConsumptionOnAtlas_SetsWarn()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "test-container");
            environment.Platform = OSPlatform.Linux;

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Warn, options.NonCronScheduleBehavior);
        }

        [Fact]
        public void Configure_LinuxConsumptionOnLegion_SetsWarn()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "test-container");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "legion-host");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);
            environment.Platform = OSPlatform.Linux;

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Warn, options.NonCronScheduleBehavior);
        }

        [Fact]
        public void Configure_NonConsumption_SetsAllow()
        {
            var environment = new TestEnvironment();

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Allow, options.NonCronScheduleBehavior);
        }

        [Fact]
        public void Configure_DedicatedAppService_SetsAllow()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId, "some-instance");
            environment.Platform = OSPlatform.Windows;

            var options = ConfigureOptions(environment);

            Assert.Equal(NonCronScheduleBehavior.Allow, options.NonCronScheduleBehavior);
        }

        private static TimerTriggerPlatformOptions ConfigureOptions(TestEnvironment environment)
        {
            var setup = new TimerTriggerPlatformOptionsSetup(environment);
            var options = new TimerTriggerPlatformOptions();
            setup.Configure(options);

            return options;
        }
    }
}
