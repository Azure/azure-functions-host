// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net.Http;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Scale;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using IScaleMetricsRepository = Microsoft.Azure.WebJobs.Script.Scale.IScaleMetricsRepository;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebScriptHostBuilderExtension
    {
        public static IHostBuilder AddWebScriptHost(this IHostBuilder builder, IServiceProvider rootServiceProvider,
           IServiceScopeFactory rootScopeFactory, ScriptApplicationHostOptions webHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            ILoggerFactory configLoggerFactory = rootServiceProvider.GetService<ILoggerFactory>();
            IDependencyValidator validator = rootServiceProvider.GetService<IDependencyValidator>();
            IMetricsLogger metricsLogger = rootServiceProvider.GetService<IMetricsLogger>();
            IEnvironment environment = rootServiceProvider.GetService<IEnvironment>();

            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory, validator))
                .ConfigureServices(services =>
                {
                    // register default configuration
                    // must happen before the script host is added below
                    services.ConfigureOptions<HttpOptionsSetup>();
                    services.ConfigureOptions<CustomHttpHeadersOptionsSetup>();
                    services.ConfigureOptions<HostHstsOptionsSetup>();
                    services.ConfigureOptions<HostCorsOptionsSetup>();
                    services.ConfigureOptions<CorsOptionsSetup>();
                    services.ConfigureOptions<AppServiceOptionsSetup>();
                    services.ConfigureOptions<HostEasyAuthOptionsSetup>();
                    services.ConfigureOptions<PrimaryHostCoordinatorOptionsSetup>();
                })
                .AddScriptHost(webHostOptions, configLoggerFactory, metricsLogger, webJobsBuilder =>
                {
                    configureWebJobs?.Invoke(webJobsBuilder);

                    webJobsBuilder.Services.TryAddSingleton<HttpClient>(f =>
                    {
                        var loggerFactory = f.GetService<ILoggerFactory>();
                        loggerFactory.CreateLogger(LogCategories.Startup).LogWarning("Using HttpClient as an injected dependency will not be supported in future versions of Azure Functions. Use IHttpClientFactory instead. See http://aka.ms/functions-httpclient-di for more information.");
                        return rootServiceProvider.GetService<HttpClient>();
                    });

                    ConfigureRegisteredBuilders(webJobsBuilder, rootServiceProvider);

                    webJobsBuilder.Services.AddSingleton<IHttpRoutesManager, WebScriptHostHttpRoutesManager>();
                })
                .ConfigureAppConfiguration(configurationBuilder =>
                {
                    ConfigureRegisteredBuilders(configurationBuilder, rootServiceProvider);
                })
                .ConfigureLogging(loggingBuilder =>
                {
                    loggingBuilder.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();

                    var hostingConfigOptions = rootServiceProvider.GetService<IOptions<FunctionsHostingConfigOptions>>();
                    loggingBuilder.AddWebJobsSystem<SystemLoggerProvider>(RestrictHostLogs(hostingConfigOptions.Value, environment));

                    if (environment.IsAzureMonitorEnabled())
                    {
                        loggingBuilder.Services.AddSingleton<ILoggerProvider, AzureMonitorDiagnosticLoggerProvider>();
                    }

                    if (!FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDiagnosticEventLogging))
                    {
                        loggingBuilder.Services.AddSingleton<ILoggerProvider, DiagnosticEventLoggerProvider>();
                        loggingBuilder.Services.TryAddSingleton<IDiagnosticEventRepository, DiagnosticEventTableStorageRepository>();
                        loggingBuilder.Services.TryAddSingleton<IDiagnosticEventRepositoryFactory, DiagnosticEventRepositoryFactory>();
                    }

                    ConfigureRegisteredBuilders(loggingBuilder, rootServiceProvider);
                })
                .ConfigureServices(services =>
                {
                    var webHostEnvironment = rootServiceProvider.GetService<IScriptWebHostEnvironment>();

                    if (FunctionsSyncManager.IsSyncTriggersEnvironment(webHostEnvironment, environment))
                    {
                        services.AddSingleton<IHostedService, FunctionsSyncService>();
                    }

                    if (!environment.IsV2CompatibilityMode())
                    {
                        new FunctionsMvcBuilder(services).AddNewtonsoftJson();
                    }

                    services.AddSingleton<HttpRequestQueue>();
                    services.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                    services.AddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();

                    services.AddSingleton<DefaultScriptWebHookProvider>();
                    services.TryAddSingleton<IScriptWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IWebHookProvider>(p => p.GetService<DefaultScriptWebHookProvider>());
                    services.TryAddSingleton<IJobHostMiddlewarePipeline, DefaultMiddlewarePipeline>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, CustomHttpHeadersMiddleware>());
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, HstsConfigurationMiddleware>());
                    if (environment.IsAnyLinuxConsumption())
                    {
                        services.AddSingleton<ICorsMiddlewareFactory, CorsMiddlewareFactory>();
                        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostCorsMiddleware>());

                        // EasyAuth must go after CORS, as CORS preflight requests can happen before authentication
                        services.TryAddEnumerable(ServiceDescriptor.Singleton<IJobHostHttpMiddleware, JobHostEasyAuthMiddleware>());
                    }
                    services.TryAddSingleton<IScaleMetricsRepository, TableStorageScaleMetricsRepository>();

                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IConcurrencyThrottleProvider, WorkerChannelThrottleProvider>());

                    // Make sure the registered IHostIdProvider is used
                    IHostIdProvider provider = rootServiceProvider.GetService<IHostIdProvider>();
                    if (provider != null)
                    {
                        services.AddSingleton<IHostIdProvider>(provider);
                    }

                    services.AddSingleton<IDelegatingHandlerProvider, DefaultDelegatingHandlerProvider>();

                    // Logging and diagnostics
                    services.AddSingleton<IMetricsLogger>(a => new NonDisposableMetricsLogger(metricsLogger));
                    services.AddSingleton<IEventCollectorProvider, FunctionInstanceLogCollectorProvider>();

                    // Hosted services
                    services.AddSingleton<IFileMonitoringService, FileMonitoringService>();
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, IFileMonitoringService>(p => p.GetService<IFileMonitoringService>()));

                    IOptions<FunctionsHostingConfigOptions> hostingConfigOptions = rootServiceProvider.GetService<IOptions<FunctionsHostingConfigOptions>>();
                    IOptionsMonitor<FunctionsHostingConfigOptions> hostingConfigOptionsMonitor = rootServiceProvider.GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>();
                    services.AddSingleton(hostingConfigOptions);
                    services.AddSingleton(hostingConfigOptionsMonitor);

                    ConfigureRegisteredBuilders(services, rootServiceProvider);
                });

            var debugStateProvider = rootServiceProvider.GetService<IDebugStateProvider>();
            if (!FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDevInDebug, environment) && debugStateProvider.InDebugMode)
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

        private static bool RestrictHostLogs(FunctionsHostingConfigOptions options, IEnvironment environment)
        {
            // Feature flag should take precedence over the host configuration
            return !FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagEnableHostLogs, environment) && options.RestrictHostLogs;
        }

        /// <summary>
        /// Used internally to register Newtonsoft formatters with our ScriptHost.
        /// </summary>
        private class FunctionsMvcBuilder : IMvcBuilder
        {
            private readonly IServiceCollection _serviceCollection;

            public FunctionsMvcBuilder(IServiceCollection serviceCollection)
            {
                _serviceCollection = serviceCollection;
            }

            public ApplicationPartManager PartManager { get; } = new ApplicationPartManager();

            public IServiceCollection Services => _serviceCollection;
        }
    }
}
