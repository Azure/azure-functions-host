// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.IO.Abstractions;
using System.Net.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Rpc;
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

            services.AddSingleton<IScriptHostManager>(s => s.GetRequiredService<WebJobsScriptHostService>());
            services.AddSingleton<IScriptWebHostEnvironment, ScriptWebHostEnvironment>();
            services.AddSingleton<IStandbyManager, StandbyManager>();
            services.TryAddSingleton<IScriptHostBuilder, DefaultScriptHostBuilder>();

            // Linux container services
            services.AddLinuxContainerServices();

            // ScriptSettingsManager should be replaced. We're setting this here as a temporary step until
            // broader configuaration changes are made:
            services.AddSingleton<ScriptSettingsManager>();
            services.AddSingleton<IEventGenerator>(p =>
            {
                var environment = p.GetService<IEnvironment>();
                if (environment.IsLinuxContainerEnvironment())
                {
                    return new LinuxContainerEventGenerator();
                }
                else if (SystemEnvironment.Instance.IsLinuxAppServiceEnvironment())
                {
                    return new LinuxAppServiceEventGenerator(new LinuxAppServiceFileLoggerFactory());
                }
                else
                {
                    return new EtwEventGenerator();
                }
            });

            // Management services
            services.AddSingleton<IWebFunctionsManager, WebFunctionsManager>();
            services.AddSingleton<IInstanceManager, InstanceManager>();
            services.AddSingleton(_ => new HttpClient());
            services.AddSingleton<IFileSystem>(_ => FileUtility.Instance);
            services.AddTransient<VirtualFileSystem>();
            services.AddTransient<VirtualFileSystemMiddleware>();

            // Secret management
            services.TryAddSingleton<ISecretManagerProvider, DefaultSecretManagerProvider>();

            // Register common services with the WebHost
            // Language Worker Hosted Services need to be intialized before WebJobsScriptHostService
            ScriptHostBuilderExtensions.AddCommonServices(services);

            // Core script host services
            services.AddSingleton<WebJobsScriptHostService>();
            services.AddSingleton<IHostedService>(s => s.GetRequiredService<WebJobsScriptHostService>());

            // Configuration
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<ScriptApplicationHostOptions>, ScriptApplicationHostOptionsSetup>());
            services.ConfigureOptions<LanguageWorkerOptionsSetup>();
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
                    var standbyManager = p.GetService<IStandbyManager>();
                    return new StandbyInitializationService(standbyManager);
                }

                return NullHostedService.Instance;
            });
        }

        private static void AddLinuxContainerServices(this IServiceCollection services)
        {
            services.AddSingleton<IHostedService>(s =>
            {
                var environment = s.GetService<IEnvironment>();
                if (environment.IsLinuxContainerEnvironment())
                {
                    var instanceManager = s.GetService<IInstanceManager>();
                    var logger = s.GetService<ILogger<LinuxContainerInitializationHostService>>();
                    return new LinuxContainerInitializationHostService(environment, instanceManager, logger);
                }

                return NullHostedService.Instance;
            });
        }
    }
}