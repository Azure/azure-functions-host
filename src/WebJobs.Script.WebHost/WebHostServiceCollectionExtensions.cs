// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO.Abstractions;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.ContainerManagement;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Management;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Azure.WebJobs.Script.WebHost.Standby;
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

            // Standby services
            services.AddStandbyServices();

            // Core script host services
            services.AddSingleton<WebJobsScriptHostService>();
            services.AddSingleton<IHostedService>(s => s.GetRequiredService<WebJobsScriptHostService>());
            services.AddSingleton<IScriptHostManager>(s => s.GetRequiredService<WebJobsScriptHostService>());
            services.AddSingleton<IScriptWebHostEnvironment, ScriptWebHostEnvironment>();

            if (SystemEnvironment.Instance.IsLinuxContainerEnvironment())
            {
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, LinuxContainerInitializationHostService>());
            }

            // ScriptSettingsManager should be replaced. We're setting this here as a temporary step until
            // broader configuaration changes are made:
            services.AddSingleton<ScriptSettingsManager>();
            services.AddSingleton<IEventGenerator>(p =>
            {
                var settingsManager = p.GetService<ScriptSettingsManager>();
                if (SystemEnvironment.Instance.IsLinuxContainerEnvironment())
                {
                    return new LinuxContainerEventGenerator();
                }
                else
                {
                    return new EtwEventGenerator();
                }
            });

            //services.AddSingleton<ILoggerProviderFactory, WebHostLoggerProviderFactory>();

            // TODO: DI (FACAVAL) Removed the previous workaround to pass a logger factory into the host resolver
            // this is no longer needed, but we need to validate log output.
            // Remove the need to have the WebHostResolver
            //services.AddSingleton<WebHostResolver>();

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

            // we want all ILoggerFactory resolution to go through WebHostResolver
            // TODO: DI (FACAVAL) This is no longer the case... perform cleanup (/cc brettsam)
            // builder.Register(ct => ct.Resolve<WebHostResolver>().GetLoggerFactory(ct.Resolve<WebHostSettings>())).As<ILoggerFactory>().ExternallyOwned();

            // Configuration
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<ScriptApplicationHostOptions>, ScriptApplicationHostOptionsSetup>());
        }

        private static void AddStandbyServices(this IServiceCollection services)
        {
            services.AddSingleton<IOptionsChangeTokenSource<ScriptApplicationHostOptions>, StandbyChangeTokenSource>();

            // Core script host service
            services.AddSingleton<IHostedService>(p =>
            {
                var hostEnvironment = p.GetService<IScriptWebHostEnvironment>();
                if (hostEnvironment.InStandbyMode)
                {
                    var applicationHostOptions = p.GetService<IOptions<ScriptApplicationHostOptions>>();
                    var loggerFactory = p.GetService<ILoggerFactory>();

                    return new StandbyInitializationService(applicationHostOptions, loggerFactory);
                }

                return NullHostedService.Instance;
            });
        }
    }
}