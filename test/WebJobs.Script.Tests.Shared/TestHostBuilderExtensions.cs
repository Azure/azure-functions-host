// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using IApplicationLifetime = Microsoft.AspNetCore.Hosting.IApplicationLifetime;

namespace Microsoft.WebJobs.Script.Tests
{
    public static class TestHostBuilderExtensions
    {
        public static IHostBuilder ConfigureDefaultTestWebScriptHost(this IHostBuilder builder, Action<ScriptApplicationHostOptions> configure = null, bool runStartupHostedServices = false, IEnvironment environment = null)
        {
            return builder.ConfigureDefaultTestWebScriptHost(null, configure, runStartupHostedServices, environment: environment);
        }

        public static IHostBuilder ConfigureDefaultTestWebScriptHost(this IHostBuilder builder, Action<IWebJobsBuilder> configureWebJobs,
            Action<ScriptApplicationHostOptions> configure = null, bool runStartupHostedServices = false, Action<IServiceCollection> configureRootServices = null, IEnvironment environment = null)
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

            if (environment is not null)
            {
                services.AddSingleton<IEnvironment>(environment);
            }
            else
            {
                AddMockedSingleton<IEnvironment>(services);
            }

            AddMockedSingleton<IDebugStateProvider>(services);
            AddMockedSingleton<IScriptHostManager>(services);
            AddMockedSingleton<IScriptWebHostEnvironment>(services);
            AddMockedSingleton<IEventGenerator>(services);
            AddMockedSingleton<IFunctionInvocationDispatcherFactory>(services);
            AddMockedSingleton<IHttpWorkerService>(services);
            AddMockedSingleton<IApplicationLifetime>(services);
            AddMockedSingleton<IDependencyValidator>(services);
            services.AddSingleton<HostNameProvider>();
            services.AddSingleton<IMetricsLogger>(metricsLogger);
            services.AddWebJobsScriptHostRouting();
            services.AddLogging();
            services.AddFunctionMetadataManager(webHostOptions, metricsLogger);
            configureRootServices?.Invoke(services);

            var rootProvider = new WebHostServiceProvider(services);

            builder
                .AddWebScriptHost(rootProvider, rootProvider, webHostOptions, configureWebJobs)
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

        private static IServiceCollection AddFunctionMetadataManager(this IServiceCollection services, ScriptApplicationHostOptions options, IMetricsLogger metricsLogger)
        {
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var source = new TestChangeTokenSource<ScriptApplicationHostOptions>();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            var hostMetadataProvider = new HostFunctionMetadataProvider(optionsMonitor, NullLogger<HostFunctionMetadataProvider>.Instance, metricsLogger);

            var workerProvider = new Mock<IWorkerFunctionMetadataProvider>();
            workerProvider.Setup(m => m.GetFunctionMetadataAsync(It.IsAny<IEnumerable<RpcWorkerConfig>>(), false)).Returns(Task.FromResult(new FunctionMetadataResult(true, ImmutableArray<FunctionMetadata>.Empty)));
            var defaultProvider = new FunctionMetadataProvider(NullLogger<FunctionMetadataProvider>.Instance, workerProvider.Object, hostMetadataProvider);
            var metadataManager = TestFunctionMetadataManager.GetFunctionMetadataManager(new OptionsWrapper<ScriptJobHostOptions>(new ScriptJobHostOptions()), defaultProvider, new List<IFunctionProvider>(), new OptionsWrapper<HttpWorkerOptions>(new HttpWorkerOptions()), new NullLoggerFactory(), new OptionsWrapper<LanguageWorkerOptions>(TestHelpers.GetTestLanguageWorkerOptions()));
            services.AddSingleton<IFunctionMetadataManager>(metadataManager);
            services.AddSingleton<IFunctionMetadataProvider>(defaultProvider);
            return services;
        }
    }
}
