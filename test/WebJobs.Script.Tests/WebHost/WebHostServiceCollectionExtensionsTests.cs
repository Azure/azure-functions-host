// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

        [Fact]
        public void AddWebJobsScriptHost_RegistersHostedServiceManagerBeforeFunctionsHostedServices_SoItStopsLastUnderLifoShutdown()
        {
            var services = new ServiceCollection();

            services.AddWebJobsScriptHost(new ConfigurationBuilder().Build());

            var hostedServiceDescriptors = services
                .Where(d => d.ServiceType == typeof(IHostedService))
                .ToList();

            int managerIndex = hostedServiceDescriptors.FindIndex(d => d.ImplementationType == typeof(HostedServiceManager));
            Assert.True(managerIndex >= 0, $"{nameof(HostedServiceManager)} should be registered as an {nameof(IHostedService)}.");

            // The Generic Host stops IHostedServices in LIFO (reverse registration) order, so the
            // first-registered service stops last. HostedServiceManager runs the language worker channel
            // shutdown and must stop last so the JobHost finishes draining in-flight invocations before the
            // worker channels are torn down. No Functions-owned hosted service may be registered before it
            // (framework services such as DataProtection are fine; they are unrelated to the drain ordering).
            var functionsAssemblies = new[]
            {
                typeof(HostedServiceManager).Assembly,
                typeof(WebJobsScriptHostService).Assembly,
            };

            for (int i = 0; i < managerIndex; i++)
            {
                var implementationType = hostedServiceDescriptors[i].ImplementationType;
                Assert.False(
                    implementationType is not null && functionsAssemblies.Contains(implementationType.Assembly),
                    $"'{implementationType}' is registered before {nameof(HostedServiceManager)} and would stop after it under the Generic Host's LIFO shutdown order.");
            }
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
