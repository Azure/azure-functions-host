// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Core;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Autofac;
using Autofac.Extensions.DependencyInjection;

namespace WebJobs.Script.WebHost.Core
{
    public static class WebJobsServiceCollectionExtensions
    {
        public static IServiceCollection AddWebJobsScriptHost(this IServiceCollection services)
        {
            services.AddWebJobsScriptHostAuth();
            services.AddWebJobsScriptHostRouting();

            services.AddMvc()
                .AddXmlDataContractSerializerFormatters();

            return services;
        }

        public static IServiceCollection AddWebJobsScriptHostRouting(this IServiceCollection services)
        {
            // Add our script route handler
            services.TryAddSingleton<IWebJobsRouteHandler, ScriptRouteHandler>();

            return services.AddHttpBindingRouting();
        }

        public static IServiceCollection AddWebJobsScriptHostAuth(this IServiceCollection services)
        {
            services.AddAuthentication()
                .AddScriptAuthLevel();

            services.AddAuthorization(o =>
            {
                o.AddScriptPolicies();
            });

            return services.AddSingleton<IAuthorizationHandler, AuthLevelAuthorizationHandler>();
        }

        public static IServiceProvider AddWebJobsScriptHostApplicationServices(this IServiceCollection services)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebJobsScriptHostService>());

            var builder = new ContainerBuilder();

            // TODO: This is a direct port from the current model.
            // Some of those services (or the way we register them) may need to change
            builder.RegisterType<DefaultSecretManagerFactory>().As<ISecretManagerFactory>().SingleInstance();
            builder.RegisterType<ScriptEventManager>().As<IScriptEventManager>().SingleInstance();
            builder.RegisterType<DefaultLoggerFactoryBuilder>().As<ILoggerFactoryBuilder>().SingleInstance();
            builder.RegisterInstance(ScriptSettingsManager.Instance);
            builder.Register(c => WebHostSettings.CreateDefault(c.Resolve<ScriptSettingsManager>()));
            builder.RegisterType<WebHostResolver>().SingleInstance();

            // The services below need to be scoped to a pseudo-tenant (warm/specialized environment)
            builder.Register<WebScriptHostManager>(c => c.Resolve<WebHostResolver>().GetWebScriptHostManager()).ExternallyOwned();
            builder.Register<ISecretManager>(c => c.Resolve<WebHostResolver>().GetSecretManager()).ExternallyOwned();

            // Populate the container builder with registered services.
            // Doing this here will cause any services registered in the service collection to
            // override the registrations above
            builder.Populate(services);

            var applicationContainer = builder.Build();

            return new AutofacServiceProvider(applicationContainer);
        }
    }
}
