// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.Azure.WebJobs.Host.Executors;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Configuration;
using Microsoft.Azure.WebJobs.Script.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Extensibility;
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

            return builder.AddScriptHost(new OptionsWrapper<ScriptApplicationHostOptions>(options), loggerFactory, null);
        }

        public static IHostBuilder AddScriptHost(this IHostBuilder builder, IOptions<ScriptApplicationHostOptions> applicationOptions, ILoggerFactory loggerFactory, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;

            // Host configuration
            builder.ConfigureLogging((context, loggingBuilder) =>
            {
                string loggingPath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging");
                loggingBuilder.AddConfiguration(context.Configuration.GetSection(loggingPath));

                loggingBuilder.Services.AddSingleton<ILoggerProvider, HostFileLoggerProvider>();
                loggingBuilder.Services.AddSingleton<ILoggerProvider, FunctionFileLoggerProvider>();

                if (ConsoleLoggingEnabled(context))
                {
                    loggingBuilder.AddConsole(c => { c.DisableColors = false; });
                }

                ConfigureApplicationInsights(context, loggingBuilder);
            })
            .ConfigureAppConfiguration(c =>
            {
                c.Add(new HostJsonFileConfigurationSource(applicationOptions, loggerFactory));
            });

            // WebJobs configuration
            return builder.AddScriptHostCore(applicationOptions, configureWebJobs);
        }

        public static IHostBuilder AddScriptHostCore(this IHostBuilder builder, IOptions<ScriptApplicationHostOptions> webHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            builder.ConfigureWebJobs(webJobsBuilder =>
            {
                // Built in binding registrations
                webJobsBuilder.AddExecutionContextBinding(o =>
                {
                    o.AppDirectory = webHostOptions.Value.ScriptPath;
                })
                .AddHttp(o =>
                {
                    o.SetResponse = HttpBinding.SetResponse;
                })
                .AddTimers()
                .AddManualTrigger();

                webJobsBuilder.UseScriptExternalStartup(webHostOptions.Value.ScriptPath);

                configureWebJobs?.Invoke(webJobsBuilder);
            }, o => o.AllowPartialHostStartup = true);

            // Script host services
            builder.ConfigureServices(services =>
            {
                // Core WebJobs/Script Host services
                services.AddSingleton<ScriptHost>();
                services.AddSingleton<IScriptJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IJobHost>(p => p.GetRequiredService<ScriptHost>());
                services.AddSingleton<IFunctionMetadataManager, FunctionMetadataManager>();
                services.AddSingleton<IProxyMetadataManager, ProxyMetadataManager>();
                services.AddSingleton<ITypeLocator, ScriptTypeLocator>();
                services.AddSingleton<IHostIdProvider, ScriptHostIdProvider>();
                services.AddSingleton<ScriptSettingsManager>();
                services.AddSingleton<IScriptEventManager, ScriptEventManager>();
                services.AddSingleton<IEnvironment>(SystemEnvironment.Instance);
                services.AddTransient<IExtensionsManager, ExtensionsManager>();
                services.TryAddSingleton<IMetricsLogger, MetricsLogger>();
                services.TryAddSingleton<IScriptJobHostEnvironment, ConsoleScriptJobHostEnvironment>();

                // Script binding providers
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, WebJobsCoreScriptBindingProvider>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, CoreExtensionsScriptBindingProvider>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IScriptBindingProvider, GeneralScriptBindingProvider>());

                // Configuration
                services.AddSingleton<IOptions<ScriptApplicationHostOptions>>(webHostOptions);
                services.ConfigureOptions<ScriptHostOptionsSetup>();

                services.AddSingleton<IDebugManager, DebugManager>();
                services.AddSingleton<IDebugStateProvider, DebugStateProvider>();
                services.AddSingleton<IFileLoggingStatusManager, FileLoggingStatusManager>();
                services.AddSingleton<IPrimaryHostStateProvider, PrimaryHostStateProvider>();

                // Hosted services
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, PrimaryHostCoordinator>());
            });

            return builder;
        }

        public static IWebJobsBuilder UseScriptExternalStartup(this IWebJobsBuilder builder, string rootScriptPath)
        {
            return builder.UseExternalStartup(new ScriptStartupTypeDiscoverer(rootScriptPath));
        }

        internal static bool ConsoleLoggingEnabled(HostBuilderContext context)
        {
            // console logging defaults to false, except for self host
            // TODO: This doesn't seem to be picking up that it's in Development when running locally.
            bool enableConsole = context.HostingEnvironment.IsDevelopment();

            string consolePath = ConfigurationPath.Combine(ConfigurationSectionNames.JobHost, "Logging", "Console", "IsEnabled");
            IConfigurationSection configSection = context.Configuration.GetSection(consolePath);

            if (configSection.Exists())
            {
                // if it has been explicitly configured that value overrides default
                enableConsole = configSection.Get<bool>();
            }

            return enableConsole;
        }

        internal static void ConfigureApplicationInsights(HostBuilderContext context, ILoggingBuilder builder)
        {
            string appInsightsKey = context.Configuration[EnvironmentSettingNames.AppInsightsInstrumentationKey];
            if (!string.IsNullOrEmpty(appInsightsKey))
            {
                builder.AddApplicationInsights(o => o.InstrumentationKey = appInsightsKey);
                builder.Services.ConfigureOptions<ApplicationInsightsLoggerOptionsSetup>();

                // Override the default SdkVersion with the functions key
                builder.Services.AddSingleton<TelemetryClient>(provider =>
                {
                    TelemetryConfiguration configuration = provider.GetService<TelemetryConfiguration>();
                    TelemetryClient client = new TelemetryClient(configuration);

                    client.Context.GetInternalContext().SdkVersion = $"azurefunctions: {ScriptHost.Version}";

                    return client;
                });
            }
        }
    }
}
