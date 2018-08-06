﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Hosting;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebScriptHostBuilderExtension
    {
        public static IHostBuilder AddWebScriptHost(this IHostBuilder builder, IServiceProvider rootServiceProvider,
           IServiceScopeFactory rootScopeFactory, IOptions<ScriptApplicationHostOptions> webHostOptions)
        {
            ILoggerFactory configLoggerFactory = rootServiceProvider.GetService<ILoggerFactory>();

            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory))
                .ConfigureLogging((context, loggingBuilder) =>
                {
                    // TODO: DI (FACAVAL) Temporary - replace with proper logger factory using
                    // job host configuration
                    loggingBuilder.Services.AddSingleton<ILoggerFactory, ScriptLoggerFactory>();

                    loggingBuilder.Services.AddSingleton<ILoggerProvider, SystemLoggerProvider>();

                    // TODO: DI (BRETTSAM) re-enable app insights
                    // If the instrumentation key is null, the call to AddApplicationInsights is a no-op.
                    string appInsightsKey = context.Configuration[EnvironmentSettingNames.AppInsightsInstrumentationKey];
                    // loggingBuilder.Services.AddApplicationInsights(appInsightsKey, (_, level) => level > LogLevel.Debug, null);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                    services.AddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();
                    // TODO: DI (FACAVAL) Review metrics logger registration
                    services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
                    services.AddSingleton<IScriptJobHostEnvironment, WebScriptJobHostEnvironment>();

                    // Secret management
                    services.AddSingleton<ISecretManager>(c => c.GetService<ISecretManagerFactory>().Create());
                    services.AddSingleton<ISecretsRepository>(c => c.GetService<ISecretsRepositoryFactory>().Create());
                    services.AddSingleton<ISecretManagerFactory, DefaultSecretManagerFactory>();
                    services.AddSingleton<ISecretsRepositoryFactory, DefaultSecretsRepositoryFactory>();
                })
                .AddScriptHost(webHostOptions, configLoggerFactory)
                .AddWebJobsLogging() // Enables WebJobs v1 classic logging
                .AddAzureStorageCoreServices();

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
    }
}
