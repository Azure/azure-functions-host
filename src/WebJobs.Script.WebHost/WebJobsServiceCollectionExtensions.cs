// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

        public static IServiceProvider AddWebJobsScriptHost(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddWebJobsScriptHostRouting();
            services.AddMvc()
                .AddXmlDataContractSerializerFormatters();

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebJobsScriptHostService>());

            // TODO: This is a direct port from the current model.
            // Some of those services (or the way we register them) may need to change
            var builder = new ContainerBuilder();

            // ScriptSettingsManager should be replaced. We're setting this here as a temporary step until
            // broader configuaration changes are made:
            builder.RegisterType<ScriptSettingsManager>().SingleInstance();

            builder.Register<IEventGenerator>(c =>
            {
                var settingsManager = c.Resolve<ScriptSettingsManager>();
                if (settingsManager.IsLinuxContainerEnvironment)
                {
                    return new LinuxContainerEventGenerator();
                }
                else
                {
                    return new EtwEventGenerator();
                }
            }).SingleInstance();

            builder.RegisterType<DefaultSecretManagerFactory>().As<ISecretManagerFactory>().SingleInstance();
            builder.RegisterType<ScriptEventManager>().As<IScriptEventManager>().SingleInstance();
            builder.Register(c => WebHostSettings.CreateDefault(c.Resolve<ScriptSettingsManager>()));

            // Pass a specially-constructed LoggerFactory to the WebHostResolver, if there isn't already a service instance
            // registered. This LoggerFactory is only used when there is no host available.
            // Only use this LoggerFactory for this constructor; use the registered ILoggerFactory below everywhere else.
            // TODO: This default logger factory is a TEMP workaround. Fix this.
            var defaultLoggerFactory = services.Where(p => p.ServiceType == typeof(ILoggerFactory) && p.ImplementationInstance != null).Select(p => p.ImplementationInstance).FirstOrDefault();
            builder.RegisterType<WebHostResolver>().SingleInstance()
                    .WithParameter(new ResolvedParameter(
                        (pi, ctx) => pi.ParameterType == typeof(ILoggerFactory),
                        (pi, ctx) => defaultLoggerFactory ?? CreateLoggerFactory(string.Empty, ctx.Resolve<ScriptSettingsManager>(), ctx.Resolve<IEventGenerator>(), ctx.Resolve<WebHostSettings>())));

            // Register the LoggerProviderFactory, which defines the ILoggerProviders for the host.
            builder.RegisterType<WebHostLoggerProviderFactory>().As<ILoggerProviderFactory>().SingleInstance();

            // Temporary - This should be replaced with a simple type registration.
            builder.Register<IExtensionsManager>(c =>
            {
                var hostInstance = c.Resolve<WebScriptHostManager>().Instance;
                return new ExtensionsManager(hostInstance.ScriptConfig.RootScriptPath, hostInstance.Logger, hostInstance.ScriptConfig.NugetFallBackPath);
            });

            // The services below need to be scoped to a pseudo-tenant (warm/specialized environment)
            builder.Register<WebScriptHostManager>(c => c.Resolve<WebHostResolver>().GetWebScriptHostManager()).ExternallyOwned();
            builder.Register<ISecretManager>(c => c.Resolve<WebHostResolver>().GetSecretManager()).ExternallyOwned();
            builder.RegisterType<WebFunctionsManager>().As<IWebFunctionsManager>().SingleInstance();
            builder.RegisterType<InstanceManager>().As<IInstanceManager>().SingleInstance();
            builder.Register(_ => new HttpClient()).SingleInstance();
            builder.RegisterType<VirtualFileSystem>();
            builder.RegisterType<VirtualFileSystemMiddleware>();

            // Populate the container builder with registered services.
            // Doing this here will cause any services registered in the service collection to
            // override the registrations above
            builder.Populate(services);

            // we want all ILoggerFactory resolution to go through WebHostResolver
            builder.Register(ct => ct.Resolve<WebHostResolver>().GetLoggerFactory(ct.Resolve<WebHostSettings>())).As<ILoggerFactory>().ExternallyOwned();

            var applicationContainer = builder.Build();

            return new AutofacServiceProvider(applicationContainer);
        }

        private static ILoggerFactory CreateLoggerFactory(string hostInstanceId, ScriptSettingsManager settingsManager, IEventGenerator eventGenerator, WebHostSettings settings)
        {
            var loggerFactory = new LoggerFactory(Enumerable.Empty<ILoggerProvider>(), Utility.CreateLoggerFilterOptions());

            var systemLoggerProvider = new SystemLoggerProvider(hostInstanceId, eventGenerator, settingsManager);
            loggerFactory.AddProvider(systemLoggerProvider);

            // This loggerFactory logs everything to host files. No filter is applied because it is created
            // before we parse host.json.
            var hostFileLogger = new HostFileLoggerProvider(hostInstanceId, settings.LogPath, () => true);
            loggerFactory.AddProvider(hostFileLogger);

            return loggerFactory;
        }
    }
}