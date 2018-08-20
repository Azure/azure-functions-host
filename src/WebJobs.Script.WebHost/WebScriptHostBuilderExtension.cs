// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Host.Timers;
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
           IServiceScopeFactory rootScopeFactory, IOptions<ScriptApplicationHostOptions> webHostOptions, Action<IWebJobsBuilder> configureWebJobs = null)
        {
            ILoggerFactory configLoggerFactory = rootServiceProvider.GetService<ILoggerFactory>();

            builder.UseServiceProviderFactory(new JobHostScopedServiceProviderFactory(rootServiceProvider, rootScopeFactory))
                .AddScriptHost(webHostOptions, configLoggerFactory, webJobsBuilder =>
                {
                    webJobsBuilder
                        .AddWebJobsLogging() // Enables WebJobs v1 classic logging
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

                    loggingBuilder.Services.AddSingleton<ILoggerProvider, SystemLoggerProvider>();

                    ConfigureRegisteredBuilders(loggingBuilder, rootServiceProvider);
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<HttpRequestQueue>();
                    services.AddSingleton<IHostLifetime, JobHostHostLifetime>();
                    services.TryAddSingleton<IWebJobsExceptionHandler, WebScriptHostExceptionHandler>();
                    services.AddSingleton<IScriptJobHostEnvironment, WebScriptJobHostEnvironment>();

                    // Logging and diagnostics
                    services.AddSingleton<IMetricsLogger, WebHostMetricsLogger>();
                    services.AddSingleton<IAsyncCollector<Host.Loggers.FunctionInstanceLogEntry>, FunctionInstanceLogger>();

                    // Secret management
                    services.TryAddSingleton<ISecretManager>(c => c.GetService<ISecretManagerFactory>().Create());
                    services.TryAddSingleton<ISecretsRepository>(c => c.GetService<ISecretsRepositoryFactory>().Create());
                    services.AddSingleton<ISecretManagerFactory, DefaultSecretManagerFactory>();
                    services.AddSingleton<ISecretsRepositoryFactory, DefaultSecretsRepositoryFactory>();

                    // Hosted services
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, HttpInitializationService>());
                    services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FileMonitoringService>());
                });

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
