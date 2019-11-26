// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
using Microsoft.Azure.WebJobs.Script.Http;
using Microsoft.Azure.WebJobs.Script.ManagedDependencies;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.Http;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
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

        public static IHostBuilder AddScriptHost(this IHostBuilder builder, ScriptApplicationHostOptions applicationOptions, Action<IWebJobsBuilder> configureWebJobs = null, IMetricsLogger metricsLogger = null)
            => builder.AddScriptHost(applicationOptions, null, metricsLogger, configureWebJobs);

        public static IHostBuilder AddScriptHost(this IHostBuilder builder, ScriptApplicationHostOptions applicationOptions, ILoggerFactory loggerFactory, IMetricsLogger metricsLogger, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            builder.SetAzureFunctionsConfigurationRoot();
            // Host configuration
            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                loggingBuilder.AddDefaultWebJobsFilters();

                string loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");
                loggingBuilder.AddConfiguration(context.Configuration.GetSection(loggingPath));

                loggingBuilder.Services.AddSingleton<IFileWriterFactory, DefaultFileWriterFactory>();
                loggingBuilder.Services.AddSingleton<ILoggerProvider, HostFileLoggerProvider>();
                loggingBuilder.Services.AddSingleton<ILoggerProvider, FunctionFileLoggerProvider>();

                loggingBuilder.AddConsoleIfEnabled(context);

                ConfigureApplicationInsights(context, loggingBuilder);
            })
            .ConfigureAppConfiguration((context, configBuilder) =>
            {
                if (!context.Properties.ContainsKey(ScriptConstants.SkipHostJsonConfigurationKey))
                {
                    configBuilder.Add(new HostJsonFileConfigurationSource(applicationOptions, SystemEnvironment.Instance, loggerFactory, metricsLogger));
                }
            });

            // WebJobs configuration
            return builder.AddScriptHostCore(applicationOptions, metricsLogger, configureWebJobs, loggerFactory);
        }

        public static IHostBuilder AddScriptHostCore(this IHostBuilder builder, ScriptApplicationHostOptions applicationHostOptions, IMetricsLogger metricsLogger, Action<IWebJobsBuilder> configureWebJobs = null, ILoggerFactory loggerFactory = null)
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
                .AddManualTrigger()
                .AddWarmup();

                var extensionBundleOptions = GetExtensionBundleOptions(context);
                var bundleManager = new ExtensionBundleManager(extensionBundleOptions, SystemEnvironment.Instance, loggerFactory);
                webJobsBuilder.Services.AddSingleton<IExtensionBundleManager>(_ => bundleManager);
                if (!skipHostInitialization)
                {
                    // Only set our external startup if we're not suppressing host initialization
                    // as we don't want to load user assemblies otherwise.
                    webJobsBuilder.UseScriptExternalStartup(applicationHostOptions, loggerFactory, bundleManager, metricsLogger);
                }

                configureWebJobs?.Invoke(webJobsBuilder);
            }, o => o.AllowPartialHostStartup = true);

            // Script host services - these services are scoped to a host instance, and when a new host
            // is created, these services are recreated
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();

                // HTTP Worker
                services.AddSingleton<IHttpWorkerProcessFactory, HttpWorkerProcessFactory>();
                services.AddSingleton<IHttpWorkerChannelFactory, HttpWorkerChannelFactory>();
                services.AddSingleton<IHttpWorkerService, DefaultHttpWorkerService>();
                // Rpc Worker
                services.AddSingleton<IJobHostRpcWorkerChannelManager, JobHostRpcWorkerChannelManager>();
                services.AddSingleton<IRpcFunctionInvocationDispatcherLoadBalancer, RpcFunctionInvocationDispatcherLoadBalancer>();

                //Worker Function Invocation dispatcher
                services.AddSingleton<IFunctionInvocationDispatcherFactory, FunctionInvocationDispatcherFactory>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<IProxyMetadataManager, ProxyMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<ScriptSettingsManager>();
                services.AddTransient<IExtensionsManager, ExtensionsManager>();
                services.TryAddSingleton<IHttpRoutesManager, DefaultHttpRouteManager>();
                services.TryAddSingleton<IMetricsLogger, MetricsLogger>();
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
                services.ConfigureOptions<HttpWorkerOptionsSetup>();
                services.ConfigureOptions<ManagedDependencyOptionsSetup>();
                services.AddOptions<FunctionResultAggregatorOptions>()
                    .Configure<IConfiguration>((o, c) =>
                    {
                        c.GetSection(ConfigurationSectionNames.JobHost)
                         .GetSection(ConfigurationSectionNames.Aggregator)
                         .Bind(o);
                    });
                services.AddOptions<ScaleOptions>()
                    .Configure<IConfiguration>((o, c) =>
                    {
                        c.GetSection(ConfigurationSectionNames.JobHost)
                         .GetSection(ConfigurationSectionNames.Scale)
                         .Bind(o);
                    });

                services.AddSingleton<IFileLoggingStatusManager, FileLoggingStatusManager>();
                services.AddSingleton<IPrimaryHostStateProvider, PrimaryHostStateProvider>();

                if (!applicationHostOptions.HasParentScope)
                {
                    AddCommonServices(services);
                }

                services.AddSingleton<IHostedService, WorkerConsoleLogService>();
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrimaryHostCoordinator>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FunctionInvocationDispatcherShutdownManager>());

                if (SystemEnvironment.Instance.IsRuntimeScaleMonitoringEnabled())
                {
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FunctionsScaleMonitorService>());
                }
                services.TryAddSingleton<FunctionsScaleManager>();
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
            services.AddManagedHostedService<RpcInitializationService>();
            services.AddSingleton<FunctionRpc.FunctionRpcBase, FunctionRpcService>();
            services.AddSingleton<IRpcServer, GrpcServer>();
            services.TryAddSingleton<IWorkerConsoleLogSource, WorkerConsoleLogSource>();
            services.AddSingleton<IWorkerProcessFactory, DefaultWorkerProcessFactory>();
            services.AddSingleton<IRpcWorkerProcessFactory, RpcWorkerProcessFactory>();
            services.AddSingleton<IRpcWorkerChannelFactory, RpcWorkerChannelFactory>();
            services.TryAddSingleton<IWebHostRpcWorkerChannelManager, WebHostRpcWorkerChannelManager>();
            services.TryAddSingleton<IDebugManager, DebugManager>();
            services.TryAddSingleton<IDebugStateProvider, DebugStateProvider>();
            services.TryAddSingleton<IEnvironment>(SystemEnvironment.Instance);
            services.TryAddSingleton<HostPerformanceManager>();
            services.ConfigureOptions<HostHealthMonitorOptionsSetup>();
            AddProcessRegistry(services);
        }

        public static IWebJobsBuilder UseScriptExternalStartup(this IWebJobsBuilder builder, ScriptApplicationHostOptions applicationHostOptions, ILoggerFactory loggerFactory, IExtensionBundleManager extensionBundleManager, IMetricsLogger metricsLogger)
        {
            var logger = loggerFactory?.CreateLogger<ScriptStartupTypeLocator>() ?? throw new ArgumentNullException(nameof(loggerFactory));
            var metadataServiceProvider = applicationHostOptions.RootServiceProvider.GetService<IFunctionMetadataProvider>();
            return builder.UseExternalStartup(new ScriptStartupTypeLocator(applicationHostOptions.ScriptPath, logger, extensionBundleManager, metadataServiceProvider, metricsLogger), loggerFactory);
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
            string appInsightsInstrumentationKey = context.Configuration[EnvironmentSettingNames.AppInsightsInstrumentationKey];
            string appInsightsConnectionString = context.Configuration[EnvironmentSettingNames.AppInsightsConnectionString];

            // Initializing AppInsights services during placeholder mode as well to avoid the cost of JITting these objects during specialization
            if (!string.IsNullOrEmpty(appInsightsInstrumentationKey) || !string.IsNullOrEmpty(appInsightsConnectionString) || SystemEnvironment.Instance.IsPlaceholderModeEnabled())
            {
                builder.AddApplicationInsightsWebJobs(o =>
                {
                    o.InstrumentationKey = appInsightsInstrumentationKey;
                    o.ConnectionString = appInsightsConnectionString;
                });

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
            if (string.Equals(Environment.GetEnvironmentVariable(RpcWorkerConstants.FunctionWorkerRuntimeSettingName), "powershell"))
            {
                builder.ConfigureServices(services =>
                {
                    services.AddSingleton<IFuncAppFileProvisionerFactory, FuncAppFileProvisionerFactory>();
                    services.AddSingleton<IHostedService, FuncAppFileProvisioningService>();
                });
            }
        }

        private static void AddProcessRegistry(IServiceCollection services)
        {
            // W3WP already manages job objects
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                && !ScriptSettingsManager.Instance.IsAppServiceEnvironment)
            {
                services.AddSingleton<IProcessRegistry, JobObjectRegistry>();
            }
            else
            {
                services.AddSingleton<IProcessRegistry, EmptyProcessRegistry>();
            }
        }
    }
}