// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Immutable;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Tests;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.CodeAnalysis.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using FunctionMetadata = Microsoft.Azure.WebJobs.Script.Description.FunctionMetadata;

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

            configure?.Invoke(webHostOptions);

            // Register root services
            var services = new ServiceCollection();
            AddMockedSingleton<IDebugStateProvider>(services);
            AddMockedSingleton<IScriptHostManager>(services);
            AddMockedSingleton<IEnvironment>(services);
            AddMockedSingleton<IScriptWebHostEnvironment>(services);
            AddMockedSingleton<IEventGenerator>(services);
            AddMockedSingleton<IFunctionDispatcher>(services);
            AddMockedSingleton<AspNetCore.Hosting.IApplicationLifetime>(services);
            AddMockedSingleton<IDependencyValidator>(services);
            services.AddSingleton<HostNameProvider>();
            services.AddWebJobsScriptHostRouting();
            services.AddLogging();
            services.AddFunctionMetadataProvider(webHostOptions);

            configureRootServices?.Invoke(services);

            var rootProvider = new WebHostServiceProvider(services);

            builder
                .AddWebScriptHost(rootProvider, rootProvider, webHostOptions, configureWebJobs)
                .ConfigureAppConfiguration(c =>
                {
                    c.AddTestSettings();
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

        private static IServiceCollection AddFunctionMetadataProvider(this IServiceCollection services, ScriptApplicationHostOptions options)
        {
            var factory = new TestOptionsFactory<ScriptApplicationHostOptions>(options);
            var source = new TestChangeTokenSource();
            var changeTokens = new[] { source };
            var optionsMonitor = new OptionsMonitor<ScriptApplicationHostOptions>(factory, changeTokens, factory);

            var workerOptions = new LanguageWorkerOptions
            {
                WorkerConfigs = TestHelpers.GetTestWorkerConfigs()
            };
            var metadataProvider = new FunctionMetadataProvider(optionsMonitor, new OptionsWrapper<LanguageWorkerOptions>(workerOptions), NullLogger<FunctionMetadataProvider>.Instance);
            return services.AddSingleton<IFunctionMetadataProvider>(metadataProvider);
        }
    }
}
