// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Microsoft.Azure.WebJobs.Script.Tests
{
    public class WebHostServiceCollectionExtensionsTests
    {
        [Fact]
        public void AddLinuxContainerServices_LinuxConsumptionOnAtlas_RegistersExpectedServices()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");

            using var provider = BuildServiceProvider(environment);

            Assert.IsType<LinuxContainerActivityPublisher>(provider.GetRequiredService<ILinuxContainerActivityPublisher>());
            Assert.Null(provider.GetService<ILinuxConsumptionMetricsTracker>());
        }

        [Fact]
        public void AddLinuxContainerServices_FlexConsumption_RegistersExpectedServices()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "TestLegionHost");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.FlexConsumptionSku);

            using var provider = BuildServiceProvider(environment);

            Assert.Same(NullLinuxContainerActivityPublisher.Instance, provider.GetRequiredService<ILinuxContainerActivityPublisher>());
            Assert.Null(provider.GetService<ILinuxConsumptionMetricsTracker>());
        }

        [Fact]
        public void AddLinuxContainerServices_LinuxConsumptionOnLegion_RegistersExpectedServices()
        {
            var environment = new TestEnvironment();
            environment.SetEnvironmentVariable(EnvironmentSettingNames.ContainerName, "TestContainer");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.LegionServiceHost, "TestLegionHost");
            environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteSku, ScriptConstants.DynamicSku);

            using var provider = BuildServiceProvider(environment);

            Assert.IsType<LinuxContainerActivityPublisher>(provider.GetRequiredService<ILinuxContainerActivityPublisher>());
            Assert.NotNull(provider.GetService<ILinuxConsumptionMetricsTracker>());
        }

        private static ServiceProvider BuildServiceProvider(IEnvironment environment)
        {
            var services = new ServiceCollection();
            services.AddSingleton(environment);
            services.AddLogging();
            services.AddOptions();
            services.AddHttpClient();
            services.AddLinuxContainerServices(environment);

            return services.BuildServiceProvider();
        }
    }
}
