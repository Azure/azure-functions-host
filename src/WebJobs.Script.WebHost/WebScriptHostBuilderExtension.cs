// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebScriptHostBuilderExtension
    {
        public static IHostBuilder AddWebScriptHost(this IHostBuilder builder, IServiceProvider rootServiceProvider,
           IServiceScopeFactory rootScopeFactory, ScriptApplicationHostOptions webHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            ILoggerFactory configLoggerFactory = rootServiceProvider.GetService<ILoggerFactory>();
            IDependencyValidator validator = rootServiceProvider.GetService<IDependencyValidator>();

            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory, validator))
                .ConfigureServices(services =>
                {
                    // register default configuration
                    // must happen before the script host is added below
                    services.ConfigureOptions<HttpOptionsSetup>();
                    services.ConfigureOptions<CustomHttpHeadersOptionsSetup>();
                    services.ConfigureOptions<HostHstsOptionsSetup>();
                })
                .AddScriptHost(webHostOptions, configLoggerFactory, webJobsBuilder =>
                {
                    webJobsBuilder
                        .AddAzureStorageCoreServices();

                    configureWebJobs?.Invoke(webJobsBuilder);

                    ConfigureRegisteredBuilders(webJobsBuilder, rootServiceProvider);
                })
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    ConfigureRegisteredBuilders(configurationBuilder, rootServiceProvider);
                })
                .ConfigureLogging(loggingBuilder =>
                {
                    loggingBuilder.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();

                    loggingBuilder.AddWebJobsSystem<SystemLoggerProvider>();
                    loggingBuilder.Services.AddSingleton<ILoggerProvider, UserLogMetricsLoggerProvider>();
                    loggingBuilder.Services.AddSingleton<ILoggerProvider, AzureMonitorDiagnosticLoggerProvider>();

                    ConfigureRegisteredBuilders(loggingBuilder, rootServiceProvider);
                })
                .ConfigureServices(services =>
                {
                    var webHostEnvironment = rootServiceProvider.GetService<IScriptWebHostEnvironment>();
                    var environment = rootServiceProvider.GetService<IEnvironment>();
                    if (FunctionsSyncManager.IsSyncTriggersEnvironment(webHostEnvironment, environment))
                    {
                        services.AddSingleton<IHostedService, FunctionsSyncService>();
                    }

                    services.AddSingleton<HttpRequestQueue>();
                    services.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                    services.AddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();
                    services.AddSingleton<IScriptJobHostEnvironment, WebScriptJobHostEnvironment>();

                    services.AddSingleton<DefaultScriptWebHookProvider>();
                    services.TryAddSingleton<IScriptWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.TryAddSingleton<IJobHostHttpMiddleware, CustomHttpHeadersMiddleware>();
                    services.TryAddSingleton<IJobHostHttpMiddleware, HstsConfigurationMiddleware>();

                    // Make sure the registered IHostIdProvider is used
                    IHostIdProvider provider = rootServiceProvider.GetService<IHostIdProvider>();
                    if (provider != null)
                    {
                        services.AddSingleton<IHostIdProvider>(provider);
                    }

                    // Logging and diagnostics
                    services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
                    services.AddSingleton<IEventCollectorProvider, FunctionInstanceLogCollectorProvider>();

                    // Hosted services
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpInitializationService>());
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FileMonitoringService>());

                    ConfigureRegisteredBuilders(services, rootServiceProvider);
                });

            var debugStateProvider = rootServiceProvider.GetService<IDebugStateProvider>();
            if (debugStateProvider.InDebugMode)
            {
                builder.UseEnvironment(EnvironmentName.Development);
            }

            return builder;
        }

        private static void ConfigureRegisteredBuilders<TBuilder>(TBuilder builder, IServiceProvider services)
        {
            foreach (IConfigureBuilder<TBuilder> configureBuilder in services.GetServices<IConfigureBuilder<TBuilder>>())
            {
                configureBuilder.Configure(builder);
            }
        }
    }
}
