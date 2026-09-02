// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Script.Composition;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.AssemblyAnalyzer;
using Microsoft.Azure.WebJobs.Script.WebHost.Composition;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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

        [Fact]
        public void AddWebJobsScriptHost_UsesStandardComposition()
        {
            var services = new ServiceCollection();

            services.AddWebJobsScriptHost(new ConfigurationBuilder().Build());

            ServiceDescriptor compositionDescriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(SelectedWorkerComposition)));
            var selectedComposition = Assert.IsType<SelectedWorkerComposition>(compositionDescriptor.ImplementationInstance);
            Assert.Same(ServerWorkerComposition.Instance, selectedComposition.Value);
            AssertService<IRpcServer, AspNetCoreGrpcServer>(services);
            AssertService<IWorkerFunctionMetadataProvider, WorkerFunctionMetadataProvider>(services);
            AssertService<IWebHostRpcWorkerChannelManager, WebHostRpcWorkerChannelManager>(services);
            AssertService<IWebHostWorkerManager, RpcWebHostWorkerManager>(services);

            ServiceDescriptor initializationService = Assert.Single(services.Where(d => d.ServiceType == typeof(RpcInitializationService)));
            Assert.Equal(ServiceLifetime.Singleton, initializationService.Lifetime);
            Assert.Equal(typeof(RpcInitializationService), initializationService.ImplementationType);
        }

        [Fact]
        public void AddWebJobsScriptHost_AllowsCompositionToRegisterServicesAndControllers()
        {
            var services = new ServiceCollection();
            var composition = new TestWorkerComposition();

            services.AddWebJobsScriptHost(new ConfigurationBuilder().Build(), composition);

            Assert.Equal(1, composition.WebHostConfigurationCount);
            var selectedComposition = Assert.IsType<SelectedWorkerComposition>(
                services.Single(d => d.ServiceType == typeof(SelectedWorkerComposition)).ImplementationInstance);
            Assert.Same(composition, selectedComposition.Value);
            AssertService<CompositionMarker, CompositionMarker>(services);

            var controllerFeature = new ControllerFeature();
            composition.MvcBuilder.PartManager.PopulateFeature(controllerFeature);
            Assert.Contains(typeof(CompositionTestController), controllerFeature.Controllers.Select(type => type.AsType()));
        }

        [Fact]
        public void AddWebJobsScriptHost_SelectedCompositionCannotBeOverriddenByLaterInterfaceRegistration()
        {
            var services = new ServiceCollection();
            var selectedComposition = new TestWorkerComposition();

            services.AddWebJobsScriptHost(new ConfigurationBuilder().Build(), selectedComposition);
            services.AddSingleton<IWorkerComposition>(new TestWorkerComposition());

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Same(selectedComposition, provider.GetRequiredService<SelectedWorkerComposition>().Value);
        }

        [Fact]
        public void AddWebJobsScriptHost_ThrowsWhenCompositionIsSelectedTwice()
        {
            var services = new ServiceCollection();
            IConfiguration configuration = new ConfigurationBuilder().Build();
            services.AddWebJobsScriptHost(configuration);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => services.AddWebJobsScriptHost(configuration));

            Assert.True(string.Equals(
                "A Functions Host composition has already been selected.",
                exception.Message,
                StringComparison.Ordinal));
        }

        [Fact]
        public void AddWebJobsScriptHost_RegistersScriptHostActivationBeforeDependentHostedServices()
        {
            var services = new ServiceCollection();

            services.AddWebJobsScriptHost(new ConfigurationBuilder().Build());

            var hostedServiceDescriptors = services.Where(d => d.ServiceType == typeof(IHostedService)).ToList();
            int assemblyAnalysisIndex = hostedServiceDescriptors.FindIndex(d => d.ImplementationType == typeof(AssemblyAnalysisService));
            Assert.True(assemblyAnalysisIndex > 0);

            ServiceDescriptor scriptHostActivation = hostedServiceDescriptors[assemblyAnalysisIndex - 1];
            using ServiceProvider serviceProvider = new ServiceCollection().BuildServiceProvider();
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => scriptHostActivation.ImplementationFactory(serviceProvider));
            Assert.True(exception.Message.Contains(typeof(WebJobsScriptHostService).FullName, StringComparison.Ordinal));
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

        private static void AssertService<TService, TImplementation>(IServiceCollection services)
        {
            ServiceDescriptor descriptor = Assert.Single(services.Where(d => d.ServiceType == typeof(TService)));
            Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
            Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
        }

        private sealed class TestWorkerComposition : IWorkerComposition
        {
            public IMvcBuilder MvcBuilder { get; private set; }

            public int WebHostConfigurationCount { get; private set; }

            public void ConfigureWebHostServices(IServiceCollection services, IMvcBuilder mvcBuilder)
            {
                WebHostConfigurationCount++;
                MvcBuilder = mvcBuilder;
                services.AddSingleton<CompositionMarker>();
                mvcBuilder.AddApplicationPart(typeof(CompositionTestController).Assembly);
            }

            public void ConfigureScriptHostServices(IServiceCollection services, System.IServiceProvider rootServiceProvider)
            {
            }
        }

        private sealed class CompositionMarker
        {
        }
    }

    public sealed class CompositionTestController : ControllerBase
    {
    }
}
