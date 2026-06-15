// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Net.Http;
using System.Runtime.InteropServices;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Functions.Platform.Metrics.LinuxConsumption;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host.Storage;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.AppCapabilities;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks;
using Microsoft.Azure.WebJobs.Script.ExtensionBundle;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Management.LinuxSpecialization;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.WebHost.Standby;
using Microsoft.Azure.WebJobs.Script.Workers;
using Microsoft.Azure.WebJobs.Script.Workers.FunctionDataCache;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc.Configuration;
using Microsoft.Azure.WebJobs.Script.Workers.SharedMemoryDataTransfer;
using Microsoft.Extensions.Azure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebHostServiceCollectionExtensions
    {
        public static IServiceCollection AddWebJobsScriptHostRouting(this IServiceCollection services)
        {
            // Add our script route handler
            services.TryAddSingleton<IWebJobsRouteHandler, ScriptRouteHandler>();

            return services.AddHttpBindingRouting();
        }

        public static IServiceCollection AddWebJobsScriptHostAuthentication(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddScriptAuthLevel()
                .AddScriptJwtBearer();

            return services;
        }

        public static IServiceCollection AddWebJobsScriptHostAuthorization(this IServiceCollection services)
        {
            services.AddAuthorization(o =>
            {
                o.AddScriptPolicies();
            });

            services.AddSingleton<IAuthorizationHandler, AuthLevelAuthorizationHandler>();
            services.AddSingleton<IAuthorizationHandler, NamedAuthLevelAuthorizationHandler>();
            return services.AddSingleton<IAuthorizationHandler, FunctionAuthorizationHandler>();
        }

        public static void AddWebJobsScriptHost(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddHttpContextAccessor();
            services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                // Brotli and Gzip compression providers are added by default.
                // Compression defaults to Brotli when the Brotli format is supported by the client
            });
            services.AddWebJobsScriptHostRouting();

            services.AddMvc(o =>
            {
                o.EnableEndpointRouting = false;
                o.Filters.Add(new ArmExtensionResourceFilter());
            })
            .AddNewtonsoftJson()
            .AddXmlDataContractSerializerFormatters();

            // Must stop last so the JobHost drains in-flight invocations before worker channels are
            // torn down (via HostedServiceManager -> RpcInitializationService). The Generic Host stops
            // IHostedServices in LIFO order, so register this before all other hosted services.
            services.AddSingleton<IHostedService, HostedServiceManager>();

            // Standby services
            services.AddStandbyServices();

            services.AddSingleton<IScriptHostManager>(s => s.GetRequiredService<WebJobsScriptHostService>());
            services.AddSingleton<IScriptWebHostEnvironment, ScriptWebHostEnvironment>();
            services.AddSingleton<IScriptApplicationLifetime, ScriptApplicationLifetime>();
            services.TryAddSingleton<IStandbyManager, StandbyManager>();
            services.TryAddSingleton<IServiceCollection>(services);
            services.TryAddSingleton<IScriptHostBuilder, DefaultScriptHostBuilder>();
            services.AddSingleton<IWorkerRuntimeResolver, WebHostWorkerRuntimeResolverAdapter>();

            // Metrics
            services.AddSingleton<IHostMetricsProvider, HostMetricsProvider>();
            services.AddSingleton<IHostMetrics, HostMetrics>();

            // Linux container services
            services.AddLinuxContainerServices();

            // ScriptSettingsManager should be replaced. We're setting this here as a temporary step until
            // broader configuration changes are made:
            services.AddSingleton<ScriptSettingsManager>();
            services.AddSingleton<IEventGenerator>(p =>
            {
                var environment = p.GetService<IEnvironment>();
                if (environment.IsAnyLinuxConsumption())
                {
                    var consoleLoggingOptions = p.GetService<IOptions<ConsoleLoggingOptions>>();
                    return new LinuxContainerEventGenerator(environment, consoleLoggingOptions);
                }
                else if (SystemEnvironment.Instance.IsLinuxAppService())
                {
                    var hostNameProvider = p.GetService<HostNameProvider>();
                    IOptions<FunctionsHostingConfigOptions> functionsHostingConfigOptions = p.GetService<IOptions<FunctionsHostingConfigOptions>>();
                    IOptions<AzureMonitorLoggingOptions> azureMonitorOptions = p.GetService<IOptions<AzureMonitorLoggingOptions>>();
                    return new LinuxAppServiceEventGenerator(new LinuxAppServiceFileLoggerFactory(), hostNameProvider, functionsHostingConfigOptions, azureMonitorOptions);
                }
                else if (environment.IsAnyKubernetesEnvironment())
                {
                    var consoleLoggingOptions = p.GetService<IOptions<ConsoleLoggingOptions>>();
                    return new KubernetesEventGenerator(consoleLoggingOptions);
                }
                else
                {
                    return new EtwEventGenerator();
                }
            });

            // Management services
            services.AddSingleton<IFunctionsSyncManager, FunctionsSyncManager>();
            services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
            services.AddSingleton<IWebFunctionsManager, WebFunctionsManager>();
            services.AddHttpClient();
            services.AddBundlesHttpClient();

            services.AddSingleton<StartupContextProvider>();
            services.AddSingleton<IFileSystem>(_ => FileUtility.Instance);
            services.AddTransient<VirtualFileSystem>();
            services.AddTransient<VirtualFileSystemMiddleware>();

            if (SystemEnvironment.Instance.IsLinuxConsumptionOnLegion() || SystemEnvironment.Instance.IsFlexConsumptionSku())
            {
                services.AddSingleton<IInstanceManager, LegionInstanceManager>();
            }
            else
            {
                // Default IInstanceManager
                services.AddSingleton<IInstanceManager, AtlasInstanceManager>();
            }

            // Logging and diagnostics
            services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
            if (!FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableDiagnosticEventLogging))
            {
                services.AddSingleton<ILoggerProvider, DiagnosticEventLoggerProvider>();
                services.TryAddSingleton<IDiagnosticEventRepository, DiagnosticEventTableStorageRepository>();
                services.TryAddSingleton<IDiagnosticEventRepositoryFactory, DiagnosticEventRepositoryFactory>();
            }

            // Secret management
            services.TryAddSingleton<ISecretManagerProvider, DefaultSecretManagerProvider>();

            // Shared memory data transfer and function data cache
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                services.AddSingleton<IMemoryMappedFileAccessor, MemoryMappedFileAccessorWindows>();
            }
            else
            {
                services.AddSingleton<IMemoryMappedFileAccessor, MemoryMappedFileAccessorUnix>();
            }
            services.AddSingleton<ISharedMemoryManager, SharedMemoryManager>();
            services.AddSingleton<IFunctionDataCache, FunctionDataCache>();

            // Grpc
            services.AddScriptGrpc();

            // Register common services with the WebHost
            // Language Worker Hosted Services need to be intialized before WebJobsScriptHostService
            ScriptHostBuilderExtensions.AddCommonServices(services);
            services.AddCommonRpcServices();

            services.AddSingleton<IHostFunctionMetadataProvider, HostFunctionMetadataProvider>();
            services.AddSingleton<IFunctionMetadataProvider, FunctionMetadataProvider>();

            // Core script host services
            services.AddSingleton<WebJobsScriptHostService>();
            services.AddSingleton<IHostedService>(s => s.GetRequiredService<WebJobsScriptHostService>());

            // Performs function assembly analysis to generete log use of unoptimized assemblies.
            services.AddSingleton<IHostedService, AssemblyAnalyzer.AssemblyAnalysisService>();

            // Performs checks to see if the sas token within the urls are expired.
            services.AddSingleton<IHostedService, Health.TokenExpirationService>();

            // Manages a diagnostic listener that subscribes to diagnostic sources setup in the host
            // and persists events in the logging infrastructure.
            services.AddSingleton<IHostedService, DiagnosticListenerService>();

            // Configuration

            // Enable options logging for WebHost-level options that implement IOptionsFormatter.
            services.AddFormattableOptionsLogging();

            // ScriptApplicationHostOptions are special in that they need to be reset on specialization, but the reset
            // must happen after the StandbyOptions have reset. For this reason, we have a special ChangeTokenSource that
            // will reset the ScriptApplicationHostOptions only after StandbyOptions have been reset.
            services.ConfigureOptions<ScriptApplicationHostOptionsSetup>();
            services.AddSingleton<IOptionsChangeTokenSource<ScriptApplicationHostOptions>, ScriptApplicationHostOptionsChangeTokenSource>();
            services.ConfigureOptions<StandbyOptionsSetup>();
            services.ConfigureOptionsWithChangeTokenSource<AppServiceOptions, AppServiceOptionsSetup, SpecializationChangeTokenSource<AppServiceOptions>>();
            services.ConfigureOptionsWithChangeTokenSource<HttpBodyControlOptions, HttpBodyControlOptionsSetup, SpecializationChangeTokenSource<HttpBodyControlOptions>>();
            services.ConfigureOptionsWithChangeTokenSource<ResponseCompressionOptions, ResponseCompressionOptionsSetup, SpecializationChangeTokenSource<ResponseCompressionOptions>>();
            services.ConfigureOptions<FlexConsumptionMetricsPublisherOptionsSetup>();
            services.ConfigureOptions<LinuxConsumptionLegionMetricsPublisherOptionsSetup>();
            services.ConfigureOptions<ConsoleLoggingOptionsSetup>();
            services.ConfigureOptions<AzureMonitorLoggingOptionsSetup>();
            services.AddHostingConfigOptions(configuration);
            services.ConfigureOptions<ExtensionRequirementOptionsSetup>();

            // Refresh WorkerConfigurationResolverOptions and LanguageWorkerOptions when RefreshWorkerOptionsChangeTokenSource is triggered.
            services.ConfigureOptionsWithChangeTokenSource<WorkerConfigurationResolverOptions, WorkerConfigurationResolverOptionsSetup, RefreshWorkerOptionsChangeTokenSource<WorkerConfigurationResolverOptions>>();
            services.ConfigureOptionsWithChangeTokenSource<LanguageWorkerOptions, LanguageWorkerOptionsSetup, RefreshWorkerOptionsChangeTokenSource<LanguageWorkerOptions>>();
            services.AddSingleton<WorkerConfigCacheInvalidator>();
            services.AddSingleton(SystemRuntimeInformation.Instance);
            services.AddSingleton<IWorkerConfigurationResolver, WorkerConfigurationResolver>();
            services.AddSingleton<IWorkerConfigurationProvider, DefaultWorkerConfigurationProvider>();
            services.AddSingleton<IWorkerConfigurationProvider, DynamicWorkerConfigurationProvider>();
            services.AddSingleton<IWorkerConfigurationProvider, ExplicitWorkerConfigurationProvider>();

            services.TryAddSingleton<IDependencyValidator, DependencyValidator>();
            services.TryAddSingleton<IJobHostMiddlewarePipeline>(s => DefaultMiddlewarePipeline.Empty);

            // Add AzureBlobStorageProvider to WebHost (also needed for ScriptHost) and AzureTableStorageProvider
            services.AddAzureStorageProviders();

            // Add health checks
            services.AddMetrics();
            services.AddHealthChecks().AddWebJobsScriptHealthChecks();

            // App Capabilities related services
            services.AddSingleton<IAppCapabilitiesStore, DefaultAppCapabilitiesStore>();
            services.AddSingleton<IOptionsChangeTokenSource<AppCapabilitiesOptions>, AppCapabilitiesChangeTokenSource>();
        }

        internal static void AddHostingConfigOptions(this IServiceCollection services, IConfiguration configuration)
        {
            services.ConfigureOptions<FunctionsHostingConfigOptionsSetup>();

            if (configuration != null)
            {
                // Refresh IOptionsMonitor<FunctionsHostingConfigOptions> when the config section changes
                var configSection = configuration.GetSection(ScriptConstants.FunctionsHostingConfigSectionName);
                services.AddSingleton<IOptionsChangeTokenSource<FunctionsHostingConfigOptions>>(new ConfigurationChangeTokenSource<FunctionsHostingConfigOptions>(configSection));
            }
        }

        private static void AddStandbyServices(this IServiceCollection services)
        {
            services.AddSingleton<IOptionsChangeTokenSource<StandbyOptions>, StandbyChangeTokenSource>();

            // Core script host service
            services.AddSingleton<IHostedService>(p =>
            {
                var standbyOptions = p.GetService<IOptionsMonitor<StandbyOptions>>();
                if (standbyOptions.CurrentValue.InStandbyMode)
                {
                    var standbyManager = p.GetService<IStandbyManager>();
                    return new StandbyInitializationService(standbyManager);
                }

                return NullHostedService.Instance;
            });
        }

        internal static void AddLinuxContainerServices(this IServiceCollection services, IEnvironment environment = null)
        {
            environment ??= SystemEnvironment.Instance;

            if (environment.IsLinuxConsumptionOnLegion())
            {
                services.AddLinuxConsumptionMetricsServices();
            }

            services.AddSingleton<IHostedService>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxConsumptionOnAtlas())
                {
                    var instanceManager = s.GetService<IInstanceManager>();
                    var logger = s.GetService<ILogger<AtlasContainerInitializationHostedService>>();
                    var startupContextProvider = s.GetService<StartupContextProvider>();
                    return new AtlasContainerInitializationHostedService(environment, instanceManager, logger, startupContextProvider);
                }
                else if (environment.IsFlexConsumptionSku() || environment.IsLinuxConsumptionOnLegion())
                {
                    var instanceManager = s.GetService<IInstanceManager>();
                    var logger = s.GetService<ILogger<LegionContainerInitializationHostedService>>();
                    var startupContextProvider = s.GetService<StartupContextProvider>();
                    return new LegionContainerInitializationHostedService(environment, instanceManager, logger, startupContextProvider);
                }

                return NullHostedService.Instance;
            });

            services.AddSingleton<IMetricsPublisher>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxConsumptionOnLegion())
                {
                    var options = s.GetService<IOptions<LinuxConsumptionLegionMetricsPublisherOptions>>();
                    var logger = s.GetService<ILogger<LinuxContainerLegionMetricsPublisher>>();
                    var metricsTracker = s.GetService<ILinuxConsumptionMetricsTracker>();
                    var standbyOptions = s.GetService<IOptionsMonitor<StandbyOptions>>();
                    var serviceProvider = s.GetService<IServiceProvider>();
                    var hostingConfigOptions = s.GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>();
                    return new LinuxContainerLegionMetricsPublisher(environment, standbyOptions, options, logger, new FileSystem(), metricsTracker, serviceProvider, hostingConfigOptions);
                }
                else if (environment.IsFlexConsumptionSku())
                {
                    var options = s.GetService<IOptions<FlexConsumptionMetricsPublisherOptions>>();
                    var standbyOptions = s.GetService<IOptionsMonitor<StandbyOptions>>();
                    var logger = s.GetService<ILogger<FlexConsumptionMetricsPublisher>>();
                    var metricsProvider = s.GetService<IHostMetricsProvider>();
                    return new FlexConsumptionMetricsPublisher(environment, standbyOptions, options, logger, new FileSystem(), metricsProvider);
                }
                else if (environment.IsLinuxMetricsPublishingEnabled())
                {
                    var logger = s.GetService<ILogger<LinuxContainerMetricsPublisher>>();
                    var standbyOptions = s.GetService<IOptionsMonitor<StandbyOptions>>();
                    var hostNameProvider = s.GetService<HostNameProvider>();
                    var hostingConfigOptions = s.GetService<IOptionsMonitor<FunctionsHostingConfigOptions>>();
                    return new LinuxContainerMetricsPublisher(environment, standbyOptions, logger, hostNameProvider, hostingConfigOptions);
                }

                return NullMetricsPublisher.Instance;
            });

            services.AddSingleton<IMeshServiceClient>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsAnyLinuxConsumption())
                {
                    var httpClientFactory = s.GetService<IHttpClientFactory>();
                    var logger = s.GetService<ILogger<MeshServiceClient>>();
                    return new MeshServiceClient(httpClientFactory, environment, logger);
                }

                return NullMeshServiceClient.Instance;
            });

            services.AddSingleton<LinuxContainerActivityPublisher>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxConsumptionOnAtlas() || environment.IsLinuxConsumptionOnLegion())
                {
                    var logger = s.GetService<ILogger<LinuxContainerActivityPublisher>>();
                    var meshInitServiceClient = s.GetService<IMeshServiceClient>();
                    var standbyOptions = s.GetService<IOptionsMonitor<StandbyOptions>>();
                    return new LinuxContainerActivityPublisher(standbyOptions, meshInitServiceClient, environment, logger);
                }

                return null;
            });

            services.AddSingleton<IHostedService>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxConsumptionOnAtlas() || environment.IsLinuxConsumptionOnLegion())
                {
                    return s.GetRequiredService<LinuxContainerActivityPublisher>();
                }

                return NullHostedService.Instance;
            });

            services.AddSingleton<ILinuxContainerActivityPublisher>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxConsumptionOnAtlas() || environment.IsLinuxConsumptionOnLegion())
                {
                    return s.GetRequiredService<LinuxContainerActivityPublisher>();
                }

                return NullLinuxContainerActivityPublisher.Instance;
            });

            services.AddSingleton<IRunFromPackageHandler, RunFromPackageHandler>();
            services.AddSingleton<IPackageDownloadHandler, PackageDownloadHandler>();
            services.AddSingleton<IManagedIdentityTokenProvider, ManagedIdentityTokenProvider>();
            services.AddSingleton<IUnZipHandler, UnZipHandler>();
            services.AddSingleton<IBashCommandHandler, BashCommandHandler>();
        }

        private static void AddAzureStorageProviders(this IServiceCollection services)
        {
            // Adds necessary Azure services to create clients
            services.AddAzureClientsCore();

            // HostAzureBlobStorageProvider depends on JobHostInternalStorageOptions to support ability to provide a SAS blob container as the Hosting container.
            // This is registered in WebJobs.Host.Storage, but since IAzureBlobStorageProvider needs to be accessible in the WebHost layer,
            // we need to register the JobHostInternalStorageOptions in the WebHost layer too, using the merged configuration implementation in ActiveHostWebJobsOptionsSetup.
            services.ConfigureOptionsWithChangeTokenSource<JobHostInternalStorageOptions, ActiveHostWebJobsOptionsSetup<JobHostInternalStorageOptions>, SpecializationChangeTokenSource<JobHostInternalStorageOptions>>();
            services.AddSingleton<IAzureBlobStorageProvider, HostAzureBlobStorageProvider>();
            services.AddSingleton<IAzureTableStorageProvider, HostAzureTableStorageProvider>();
        }

        private static IServiceCollection ConfigureOptionsWithChangeTokenSource<TOptions, TOptionsSetup, TOptionsChangeTokenSource>(this IServiceCollection services)
            where TOptions : class
            where TOptionsSetup : class, IConfigureOptions<TOptions>
            where TOptionsChangeTokenSource : class, IOptionsChangeTokenSource<TOptions>
        {
            services.ConfigureOptions<TOptionsSetup>();
            services.AddSingleton<IOptionsChangeTokenSource<TOptions>, TOptionsChangeTokenSource>();

            return services;
        }
    }
}