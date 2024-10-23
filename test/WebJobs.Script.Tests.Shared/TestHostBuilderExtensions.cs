// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Metrics;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Profiles;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostBuilderExtensions
    {
        public static IHostBuilder ConfigureDefaultTestWebScriptHost(this IHostBuilder builder, Action<ScriptApplicationHostOptions> configure = null, bool runStartupHostedServices = false)
        {
            return builder.ConfigureDefaultTestWebScriptHost(null, configure, runStartupHostedServices);
        }

        public static IHostBuilder ConfigureDefaultTestWebScriptHost(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs,
            Action<ScriptApplicationHostOptions> configure = null, bool runStartupHostedServices = false, Action<IServiceCollection> configureRootServices = null)
        {
            var webHostOptions = new ScriptApplicationHostOptions()
            {
                IsSelfHost = true,
                ScriptPath = TestHelpers.FunctionsTestDirectory,
                LogPath = TestHelpers.GetHostLogFileDirectory().FullName
            };
            TestMetricsLogger metricsLogger = new TestMetricsLogger();
            configure?.Invoke(webHostOptions);

            // Register root services
            var services = new ServiceCollection();
            services.AddOptions<ScriptApplicationHostOptions>()
                .Configure(o =>
                {
                    o.IsSelfHost = webHostOptions.IsSelfHost;
                    o.ScriptPath = webHostOptions.ScriptPath;
                    o.LogPath = webHostOptions.LogPath;
                });
            AddMockedSingleton<IDebugStateProvider>(services);
            AddMockedSingleton<IScriptHostManager>(services);
            AddMockedSingleton<IEnvironment>(services);
            AddMockedSingleton<IScriptWebHostEnvironment>(services);
            AddMockedSingleton<IEventGenerator>(services);
            AddMockedSingleton<IFunctionInvocationDispatcherFactory>(services);
            AddMockedSingleton<IHttpWorkerService>(services);
            AddMockedSingleton<IApplicationLifetime>(services);
            AddMockedSingleton<IDependencyValidator>(services);
            AddMockedSingleton<ISharedMemoryManager>(services);
            AddMockedSingleton<IFunctionDataCache>(services);
            AddMockedSingleton<IAzureBlobStorageProvider>(services);
            AddMockedSingleton<IAzureTableStorageProvider>(services);
            services.AddSingleton<IWorkerProfileManager, WorkerProfileManager>();
            services.AddSingleton<IDiagnosticEventRepository, TestDiagnosticEventRepository>();
            services.AddSingleton<IDiagnosticEventRepositoryFactory, TestDiagnosticEventRepositoryFactory>();
            services.AddSingleton<ISecretManagerProvider, TestSecretManagerProvider>();
            services.AddSingleton<HostNameProvider>();
            services.AddSingleton<IMetricsLogger>(metricsLogger);
            services.AddWebJobsScriptHostRouting();
            services.AddLogging();
            services.AddFunctionMetadataManager();
            services.AddHostMetrics();
            services.AddConfiguration();
            services.ConfigureOptions<LanguageWorkerOptionsSetup>();

            configureRootServices?.Invoke(services);

            var rootProvider = services.BuildServiceProvider();

            builder
                .AddWebScriptHost(rootProvider, services, webHostOptions, configureWebJobs)
                .ConfigureAppConfiguration(c =>
                {
                    c.AddTestSettings();
                })
                .ConfigureServices(s =>
                {
                    s.AddScriptGrpc();
                });

            if (!runStartupHostedServices)
            {
                builder.ConfigureServices(s => s.RemoveAll<IHostedService>());
            }

            webHostOptions.RootServiceProvider = rootProvider;
            return builder;
        }

        public static IServiceCollection AddMockedSingleton<T>(IServiceCollection services) where T : class
        {
            var mock = new Mock<T>();
            return services.AddSingleton<T>(mock.Object);
        }

        private static IServiceCollection AddConfiguration(this IServiceCollection services)
        {
            var builder = new ConfigurationBuilder();
            services.AddSingleton<IConfiguration>(builder.Build());
            return services;
        }

        private static IServiceCollection AddHostMetrics(this IServiceCollection services)
        {
            services.AddMetrics();
            services.AddSingleton<IHostMetrics, HostMetrics>();
            return services;
        }

        private static IServiceCollection AddFunctionMetadataManager(this IServiceCollection services)
        {
            var workerMetadataProvider = new Mock<IWorkerFunctionMetadataProvider>();
            workerMetadataProvider.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false)).Returns(Task.FromResult(new FunctionMetadataResult(true, ImmutableArray<FunctionMetadata>.Empty)));

            services.AddSingleton<IHostFunctionMetadataProvider, HostFunctionMetadataProvider>();
            services.AddSingleton<IFunctionMetadataProvider, FunctionMetadataProvider>();
            services.AddSingleton<IWorkerFunctionMetadataProvider>(workerMetadataProvider.Object);

            services.AddSingleton<IScriptHostManager>(s =>
            {
                var managerMock = new Mock<IScriptHostManager>();
                managerMock
                    .As<IServiceProvider>()
                    .Setup(m => m.GetService(typeof(IOptionsMonitor<LanguageWorkerOptions>)))
                    .Returns(s.GetService<IOptionsMonitor<LanguageWorkerOptions>>);
                return managerMock.Object;
            });

            services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();

            return services;
        }
    }
}
