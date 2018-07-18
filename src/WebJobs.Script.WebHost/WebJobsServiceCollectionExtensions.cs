// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using Autofac;
using Autofac.Core;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.BindingExtensions;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Serialization;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public static class WebJobsServiceCollectionExtensions
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
                .AddArmToken()
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
            services.AddWebJobsScriptHostRouting();
            services.AddMvc()
                .AddXmlDataContractSerializerFormatters();

            // Core script host service
            services.AddSingleton<WebJobsScriptHostService>();
            services.AddSingleton<IHostedService>(s => s.GetRequiredService<WebJobsScriptHostService>());

            if (EnvironmentUtility.IsLinuxContainerEnvironment)
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LinuxContainerInitializationHostService>());
            }

            // ScriptSettingsManager should be replaced. We're setting this here as a temporary step until
            // broader configuaration changes are made:
            services.AddSingleton<ScriptSettingsManager>();
            services.AddSingleton<IEventGenerator>(p =>
            {
                var settingsManager = p.GetService<ScriptSettingsManager>();
                if (EnvironmentUtility.IsLinuxContainerEnvironment)
                {
                    return new LinuxContainerEventGenerator();
                }
                else
                {
                    return new EtwEventGenerator();
                }
            });

            services.AddSingleton<ILoggerProviderFactory, WebHostLoggerProviderFactory>();

            // TODO: DI (FACAVAL) Removed the previous workaround to pass a logger factory into the host resolver
            // this is no longer needed, but we need to validate log output.
            // Remove the need to have the WebHostResolver
            //services.AddSingleton<WebHostResolver>();

            // Temporary - This should be replaced with a simple type registration.
            services.AddTransient<IExtensionsManager>(c =>
            {
                var hostInstance = c.GetService<WebScriptHostManager>().Instance;
                return new ExtensionsManager(hostInstance.ScriptOptions.RootScriptPath, hostInstance.Logger, hostInstance.ScriptOptions.NugetFallBackPath);
            });

            // TODO: DI (FACAVAL) This will be replacet by the hosted service implementation
            services.AddSingleton<IScriptHostManager, Host.TempScriptHostManager>();

            // The services below need to be scoped to a pseudo-tenant (warm/specialized environment)
            // TODO: DI (FACAVAL) This will need the child container/scoping logic for warm/specialized hosts
            //services.AddSingleton<WebScriptHostManager>(c => c.GetService<WebHostResolver>().GetWebScriptHostManager());

            // Management services
            services.AddSingleton<IWebFunctionsManager, WebFunctionsManager>();
            services.AddSingleton<IInstanceManager, InstanceManager>();

            services.AddSingleton(_ => new HttpClient());
            services.AddSingleton<IFileSystem>(_ => FileUtility.Instance);
            services.AddTransient<VirtualFileSystem>();
            services.AddTransient<VirtualFileSystemMiddleware>();

            // TODO: DI (FACAVAL) Replace with the actual web host environment
            services.AddSingleton<IScriptHostEnvironment, NullScriptHostEnvironment>();
            // we want all ILoggerFactory resolution to go through WebHostResolver
            // TODO: DI (FACAVAL) This is no longer the case... perform cleanup (/cc brettsam)
            // builder.Register(ct => ct.Resolve<WebHostResolver>().GetLoggerFactory(ct.Resolve<WebHostSettings>())).As<ILoggerFactory>().ExternallyOwned();

            // Configuration
            services.TryAddEnumerable(ServiceDescriptor.Transient<IConfigureOptions<ScriptWebHostOptions>, ScriptWebHostOptionsSetup>());
        }

        // TODO: DI (FACAVAL) Removing this. We need to ensure system logs are properly written now when using the default provider.
        //private static ILoggerFactory CreateLoggerFactory(string hostInstanceId, ScriptSettingsManager settingsManager, IEventGenerator eventGenerator, WebHostSettings settings)
        //{
        //    var loggerFactory = new LoggerFactory(Enumerable.Empty<ILoggerProvider>(), Utility.CreateLoggerFilterOptions());

        //    var systemLoggerProvider = new SystemLoggerProvider(hostInstanceId, eventGenerator, settingsManager);
        //    loggerFactory.AddProvider(systemLoggerProvider);

        //    // This loggerFactory logs everything to host files. No filter is applied because it is created
        //    // before we parse host.json.
        //    var hostFileLogger = new HostFileLoggerProvider(hostInstanceId, settings.LogPath, () => true);
        //    loggerFactory.AddProvider(hostFileLogger);

        //    return loggerFactory;
        //}
    }
}