// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class ScriptHostBuilderExtensions
    {
        public static IHostBuilder AddScriptHost(this IHostBuilder builder, IServiceProvider rootServiceProvider,
            IServiceScopeFactory rootScopeFactory, IOptions<ScriptWebHostOptions> webHostOptions)
        {
            // Host configuration
            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory))
                .ConfigureLogging((context, loggingBuilder) =>
                {
                    // TODO: DI (FACAVAL) Temporary - replace with proper logger factory using
                    // job host configuration
                    loggingBuilder.Services.AddSingleton<ILoggerFactory, CustomFactory>();

                    loggingBuilder.Services.AddSingleton<ILoggerProvider, SystemLoggerProvider>();
                    loggingBuilder.Services.AddSingleton<ILoggerProvider, HostFileLoggerProvider>();
                    loggingBuilder.Services.AddSingleton<ILoggerProvider, FunctionFileLoggerProvider>();

                    if (ConsoleLoggingEnabled(context))
                    {
                        loggingBuilder.AddConsole(c => { c.DisableColors = false; });
                        loggingBuilder.SetMinimumLevel(LogLevel.Trace);
                        loggingBuilder.AddFilter(f => true);
                    }

                    // If the instrumentation key is null, the call to AddApplicationInsights is a no-op.
                    string appInsightsKey = context.Configuration[EnvironmentSettingNames.AppInsightsInstrumentationKey];
                    loggingBuilder.Services.AddApplicationInsights(appInsightsKey, (_, level) => level > LogLevel.Debug, null);
                })
                .ConfigureServices((context, s) =>
                {
                    s.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                })
                .ConfigureAppConfiguration(c =>
                {
                    c.Add(new HostJsonFileConfigurationSource(webHostOptions));
                });

            // WebJobs configuration
            builder.AddScriptHostCore(webHostOptions);

            // HACK: Remove previous IHostedService registration
            // TODO: DI (FACAVAL) Remove this and move HttpInitialization to webjobs configuration
            builder.ConfigureServices(s =>
            {
                s.RemoveAll<IHostedService>();
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrimaryHostCoordinator>());
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, JobHostService>());
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpInitializationService>());
                s.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FileMonitoringService>());
            });

            // If there is a script host builder registered, allow it to configure
            // the host builder
            var scriptBuilder = rootServiceProvider.GetService<IScriptHostBuilder>();
            scriptBuilder?.Configure(builder);

            return builder;
        }

        public static IHostBuilder AddScriptHostCore(this IHostBuilder builder, IOptions<ScriptWebHostOptions> webHostOptions)
        {
            builder.ConfigureWebJobsHost(o =>
             {
                 o.AllowPartialHostStartup = true;
             })
             .UseScriptExternalStartup(webHostOptions.Value.ScriptPath)
             .AddWebJobsLogging() // Enables WebJobs v1 classic logging
             .AddAzureStorageCoreServices();

            // Built in binding registrations
            builder.AddExecutionContextBinding(o =>
             {
                 o.AppDirectory = webHostOptions.Value.ScriptPath;
             })
             .AddAzureStorage()
             .AddHttp(o =>
             {
                 o.SetResponse = HttpBinding.SetResponse;
             })
             .AddManualTrigger();

            // Script host services
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<IHostIdProvider, IdProvider>();
                services.AddSingleton<ScriptSettingsManager>();
                services.AddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();
                // TODO: DI (FACAVAL) Review metrics logger registration
                services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
                services.AddSingleton<IScriptEventManager, ScriptEventManager>();
                services.AddSingleton<IScriptJobHostEnvironment, WebScriptJobHostEnvironment>();
                services.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
                services.AddTransient<IExtensionsManager, ExtensionsManager>();

                // Script binding providers
                services.AddSingleton<IScriptBindingProvider, WebJobsCoreScriptBindingProvider>();
                services.AddSingleton<IScriptBindingProvider, CoreExtensionsScriptBindingProvider>();
                services.AddSingleton<IScriptBindingProvider, GeneralScriptBindingProvider>();

                // Secret management
                services.AddSingleton<ISecretManager>(c => c.GetService<ISecretManagerFactory>().Create());
                services.AddSingleton<ISecretsRepository>(c => c.GetService<ISecretsRepositoryFactory>().Create());
                services.AddSingleton<ISecretManagerFactory, DefaultSecretManagerFactory>();
                services.AddSingleton<ISecretsRepositoryFactory, DefaultSecretsRepositoryFactory>();

                // Configuration
                services.AddSingleton<IOptions<ScriptWebHostOptions>>(webHostOptions);
                services.ConfigureOptions<ScriptHostOptionsSetup>();

                services.AddSingleton<IDebugManager, DebugManager>();
                services.AddSingleton<IDebugStateProvider, DebugStateProvider>();
                services.AddSingleton<IFileLoggingStatusManager, FileLoggingStatusManager>();
                services.AddSingleton<IPrimaryHostStateProvider, PrimaryHostStateProvider>();
            });

            return builder;
        }

        public static IHostBuilder UseScriptExternalStartup(this IHostBuilder builder, string rootScriptPath)
        {
            return builder.UseExternalStartup(new ScriptStartupTypeDiscoverer(rootScriptPath));
        }

        internal static bool ConsoleLoggingEnabled(HostBuilderContext context)
        {
            // console logging defaults to false, except for self host
            // TODO: This doesn't seem to be picking up that it's in Development when running locally.
            bool enableConsole = context.HostingEnvironment.IsDevelopment();

            string configValue = context.Configuration.GetSection(ScriptConstants.ConsoleLoggingMode).Value;
            if (!string.IsNullOrEmpty(configValue))
            {
                // if it has been explicitly configured that value overrides default
                enableConsole = string.Compare(configValue, "always", StringComparison.OrdinalIgnoreCase) == 0 ? true : false;
            }

            return enableConsole;
        }
    }
}
