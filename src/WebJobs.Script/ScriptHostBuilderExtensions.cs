// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Configuration;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script
{
    public static class ScriptHostBuilderExtensions
    {
        public static IHostBuilder AddScriptHost(this IHostBuilder builder, Action<ScriptApplicationHostOptions> configureOptions, ILoggerFactory loggerFactory = null)
        {
            if (configureOptions == null)
            {
                throw new ArgumentNullException(nameof(configureOptions));
            }

            ScriptApplicationHostOptions options = new ScriptApplicationHostOptions();

            configureOptions(options);

            return builder.AddScriptHost(options, loggerFactory, null);
        }

        public static IHostBuilder AddScriptHost(this IHostBuilder builder, ScriptApplicationHostOptions applicationOptions, Action<IWebJobsBuilder> configureWebJobs = null)
            => builder.AddScriptHost(applicationOptions, null, configureWebJobs);

        public static IHostBuilder AddScriptHost(this IHostBuilder builder, ScriptApplicationHostOptions applicationOptions, ILoggerFactory loggerFactory, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            builder.SetAzureFunctionsConfigurationRoot();

            // Host configuration
            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.AddDefaultWebJobsFilters();

                string loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");
                loggingBuilder.AddConfiguration(context.Configuration.GetSection(loggingPath));

                loggingBuilder.Services.AddSingleton<ILoggerProvider, HostFileLoggerProvider>();
                loggingBuilder.Services.AddSingleton<ILoggerProvider, FunctionFileLoggerProvider>();

                loggingBuilder.AddConsoleIfEnabled(context);

                ConfigureApplicationInsights(context, loggingBuilder);
            })
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                if (!context.Properties.ContainsKey(ScriptConstants.SkipHostJsonConfigurationKey))
                {
                    configBuilder.Add(new HostJsonFileConfigurationSource(applicationOptions, SystemEnvironment.Instance, loggerFactory));
                }
            });

            // WebJobs configuration
            return builder.AddScriptHostCore(applicationOptions, configureWebJobs);
        }

        public static IHostBuilder AddScriptHostCore(this IHostBuilder builder, ScriptApplicationHostOptions applicationHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            var skipHostInitialization = builder.Properties.ContainsKey(ScriptConstants.SkipHostInitializationKey);

            builder.ConfigureWebJobs(webJobsBuilder =>
            {
                // Built in binding registrations
                webJobsBuilder.AddExecutionContextBinding(o =>
                {
                    o.AppDirectory = applicationHostOptions.ScriptPath;
                })
                .AddHttp(o =>
                {
                    o.SetResponse = HttpBinding.SetResponse;
                })
                .AddTimers()
                .AddManualTrigger();

                if (!skipHostInitialization)
                {
                    // Only set our external startup if we're not suppressing host initialization
                    // as we don't want to load user assemblies otherwise.
                    webJobsBuilder.UseScriptExternalStartup(applicationHostOptions.ScriptPath);
                }

                configureWebJobs?.Invoke(webJobsBuilder);
            }, o => o.AllowPartialHostStartup = true);

            // Script host services - these services are scoped to a host instance, and when a new host
            // is created, these services are recreated
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<IProxyMetadataManager, ProxyMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<ScriptSettingsManager>();
                services.AddTransient<IExtensionsManager, ExtensionsManager>();
                services.TryAddSingleton<IMetricsLogger, MetricsLogger>();
                services.TryAddSingleton<IScriptJobHostEnvironment, ConsoleScriptJobHostEnvironment>();

                // Script binding providers
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, WebJobsCoreScriptBindingProvider>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, CoreExtensionsScriptBindingProvider>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, GeneralScriptBindingProvider>());

                // Configuration
                services.AddSingleton<IOptions<ScriptApplicationHostOptions>>(new OptionsWrapper<ScriptApplicationHostOptions>(applicationHostOptions));
                services.AddSingleton<IOptionsMonitor<ScriptApplicationHostOptions>>(new ScriptApplicationHostOptionsMonitor(applicationHostOptions));
                services.ConfigureOptions<ScriptHostOptionsSetup>();
                services.ConfigureOptions<JobHostFunctionTimeoutOptionsSetup>();
                // TODO: pgopa only add this to WebHostServiceCollection
                services.ConfigureOptions<LanguageWorkerOptionsSetup>();
                services.AddOptions<FunctionResultAggregatorOptions>()
                    .Configure<IConfiguration>((o, c) =>
                    {
                        c.GetSection(ConfigurationSectionNames.JobHost)
                         .GetSection(ConfigurationSectionNames.Aggregator)
                         .Bind(o);
                    });

                services.AddSingleton<IFileLoggingStatusManager, FileLoggingStatusManager>();
                services.AddSingleton<IPrimaryHostStateProvider, PrimaryHostStateProvider>();

                if (!applicationHostOptions.HasParentScope)
                {
                    AddCommonServices(services);
                }

                // Hosted services
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrimaryHostCoordinator>());
            });

            return builder;
        }

        public static void AddCommonServices(IServiceCollection services)
        {
            // The scope for these services is beyond a single host instance.
            // They are not recreated for each new host instance, so you have
            // to be careful with caching, etc. E.g. these services will get
            // initially created in placeholder mode and live on through the
            // specialized app.
            services.AddSingleton<IHostIdProvider, ScriptHostIdProvider>();
            services.TryAddSingleton<IScriptEventManager, ScriptEventManager>();
            services.TryAddSingleton<IDebugManager, DebugManager>();
            services.TryAddSingleton<IDebugStateProvider, DebugStateProvider>();
            services.TryAddSingleton<IEnvironment>(SystemEnvironment.Instance);
            services.TryAddSingleton<HostPerformanceManager>();
            services.ConfigureOptions<HostHealthMonitorOptionsSetup>();
        }

        public static IWebJobsBuilder UseScriptExternalStartup(this IWebJobsBuilder builder, string rootScriptPath)
        {
            return builder.UseExternalStartup(new ScriptStartupTypeLocator(rootScriptPath));
        }

        public static IHostBuilder SetAzureFunctionsEnvironment(this IHostBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }

            string azureFunctionsEnvironment = Environment.GetEnvironmentVariable(EnvironmentSettingNames.EnvironmentNameKey);

            if (azureFunctionsEnvironment != null)
            {
                builder.UseEnvironment(azureFunctionsEnvironment);
            }

            return builder;
        }

        public static IHostBuilder SetAzureFunctionsConfigurationRoot(this IHostBuilder builder)
        {
            builder.ConfigureAppConfiguration(c =>
             {
                 c.AddInMemoryCollection(new Dictionary<string, string>
                    {
                        { "AzureWebJobsConfigurationSection", ConfigurationSectionNames.JobHost }
                    });
             });

            return builder;
        }

        internal static void ConfigureApplicationInsights(HostBuilderContext context, ILoggingBuilder builder)
        {
            string appInsightsKey = context.Configuration[EnvironmentSettingNames.AppInsightsInstrumentationKey];
            if (!string.IsNullOrEmpty(appInsightsKey))
            {
                builder.AddApplicationInsights(o => o.InstrumentationKey = appInsightsKey);
                builder.Services.ConfigureOptions<ApplicationInsightsLoggerOptionsSetup>();

                builder.Services.AddSingleton<ISdkVersionProvider, ApplicationInsightsSdkVersionProvider>();

                // Override the default SdkVersion with the functions key
                builder.Services.AddSingleton<TelemetryClient>(provider =>
                {
                    TelemetryConfiguration configuration = provider.GetService<TelemetryConfiguration>();
                    TelemetryClient client = new TelemetryClient(configuration);

                    ISdkVersionProvider versionProvider = provider.GetService<ISdkVersionProvider>();
                    client.Context.GetInternalContext().SdkVersion = versionProvider.GetSdkVersion();

                    return client;
                });
            }
        }
    }
}
