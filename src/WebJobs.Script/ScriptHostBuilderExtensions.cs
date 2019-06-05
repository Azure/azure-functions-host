// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights.DependencyCollector;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Logging.ApplicationInsights;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.FileProvisioning;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Grpc.Messages;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
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
            return builder.AddScriptHostCore(applicationOptions, configureWebJobs, loggerFactory);
        }

        public static IHostBuilder AddScriptHostCore(this IHostBuilder builder, ScriptApplicationHostOptions applicationHostOptions, Action<IWebJobsBuilder> configureWebJobs = null, ILoggerFactory loggerFactory = null)
        {
            var skipHostInitialization = builder.Properties.ContainsKey(ScriptConstants.SkipHostInitializationKey);
            builder.ConfigureWebJobs((context, webJobsBuilder) =>
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

                var extensionBundleOptions = GetExtensionBundleOptions(context);
                var bundleManager = new ExtensionBundleManager(extensionBundleOptions, SystemEnvironment.Instance, loggerFactory);
                if (!skipHostInitialization)
                {
                    // Only set our external startup if we're not suppressing host initialization
                    // as we don't want to load user assemblies otherwise.
                    webJobsBuilder.UseScriptExternalStartup(applicationHostOptions.ScriptPath, bundleManager);
                }
                webJobsBuilder.Services.AddSingleton<IExtensionBundleManager>(_ => bundleManager);

                configureWebJobs?.Invoke(webJobsBuilder);
            }, o => o.AllowPartialHostStartup = true);

            // Script host services - these services are scoped to a host instance, and when a new host
            // is created, these services are recreated
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IFunctionDispatcher, FunctionDispatcher>();
                services.AddSingleton<IFunctionDispatcherLoadBalancer, FunctionDispatcherLoadBalancer>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<IProxyMetadataManager, ProxyMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<ScriptSettingsManager>();
                services.AddTransient<IExtensionsManager, ExtensionsManager>();
                services.TryAddSingleton<IMetricsLogger, MetricsLogger>();
                services.TryAddSingleton<IScriptJobHostEnvironment, ConsoleScriptJobHostEnvironment>();
                services.AddTransient<IExtensionBundleContentProvider, ExtensionBundleContentProvider>();

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
                services.ConfigureOptions<ManagedDependencyOptionsSetup>();
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

                services.AddSingleton<IHostedService, LanguageWorkerConsoleLogService>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrimaryHostCoordinator>());
            });

            RegisterFileProvisioningService(builder);
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

            // Add Language Worker Service
            // Need to maintain the order: Add RpcInitializationService before core script host services
            services.AddSingleton<IHostedService, RpcInitializationService>();
            services.AddSingleton<FunctionRpc.FunctionRpcBase, FunctionRpcService>();
            services.AddSingleton<IRpcServer, GrpcServer>();
            services.TryAddSingleton<ILanguageWorkerConsoleLogSource, LanguageWorkerConsoleLogSource>();
            services.AddSingleton<ILanguageWorkerProcessManager, LanguageWorkerProcessManager>();
            services.TryAddSingleton<ILanguageWorkerChannelManager, LanguageWorkerChannelManager>();
            services.TryAddSingleton<IDebugManager, DebugManager>();
            services.TryAddSingleton<IDebugStateProvider, DebugStateProvider>();
            services.TryAddSingleton<IEnvironment>(SystemEnvironment.Instance);
            services.TryAddSingleton<HostPerformanceManager>();
            services.ConfigureOptions<HostHealthMonitorOptionsSetup>();
        }

        public static IWebJobsBuilder UseScriptExternalStartup(this IWebJobsBuilder builder, string rootScriptPath, IExtensionBundleManager extensionBundleManager)
        {
            return builder.UseExternalStartup(new ScriptStartupTypeLocator(rootScriptPath, extensionBundleManager));
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

            // Initializing AppInsights services during placeholder mode as well to avoid the cost of JITting these objects during specialization
            if (!string.IsNullOrEmpty(appInsightsKey) || SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                builder.AddApplicationInsights(o => o.InstrumentationKey = appInsightsKey);
                builder.Services.ConfigureOptions<ApplicationInsightsLoggerOptionsSetup>();

                builder.Services.AddSingleton<ISdkVersionProvider, FunctionsSdkVersionProvider>();

                builder.Services.AddSingleton<ITelemetryInitializer, ScriptTelemetryInitializer>();

                if (SystemEnvironment.Instance.IsPlaceholderModeEnabled())
                {
                    for (int i = 0; i < builder.Services.Count; i++)
                    {
                        // This is to avoid possible race condition during specialization when disposing old AI listeners created during placeholder mode.
                        if (builder.Services[i].ServiceType == typeof(ITelemetryModule) && builder.Services[i].ImplementationFactory?.Method.ReturnType == typeof(DependencyTrackingTelemetryModule))
                        {
                            builder.Services.RemoveAt(i);
                            break;
                        }
                    }

                    // Disable auto-http tracking when in placeholder mode.
                    builder.Services.Configure<ApplicationInsightsLoggerOptions>(o =>
                    {
                        o.HttpAutoCollectionOptions.EnableHttpTriggerExtendedInfoCollection = false;
                    });
                }
            }
        }

        internal static ExtensionBundleOptions GetExtensionBundleOptions(HostBuilderContext context)
        {
            var options = new ExtensionBundleOptions();
            var optionsSetup = new ExtensionBundleConfigurationHelper(context.Configuration, SystemEnvironment.Instance);
            context.Configuration.Bind(options);
            optionsSetup.Configure(options);
            return options;
        }

        private static void RegisterFileProvisioningService(IHostBuilder builder)
        {
            if (string.Equals(Environment.GetEnvironmentVariable(LanguageWorkerConstants.FunctionWorkerRuntimeSettingName), "powershell"))
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IFuncAppFileProvisionerFactory, FuncAppFileProvisionerFactory>();
                    services.AddSingleton<IHostedService, FuncAppFileProvisioningService>();
                });
            }
        }
    }
}
