// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Autofac;
using Autofac.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Core;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization.Policies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WebJobs.Script.WebHost.Core
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public IServiceProvider ConfigureServices(IServiceCollection services)
        {
            services.AddAuthentication()
                .AddScriptAuthLevel();

            services.AddAuthorization(o =>
            {
                o.AddScriptPolicies();
            });

            services.AddSingleton<IAuthorizationHandler, AuthLevelAuthorizationHandler>();

            services.AddWebJobsScriptHost();

            services.AddMvc()
                .AddXmlDataContractSerializerFormatters();

            return RegisterApplicationservices(services);
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IApplicationLifetime applicationLifetime, IHostingEnvironment env, ILoggerFactory loggerFactory)
        {
            app.UseWebJobsScriptHost(applicationLifetime);
            app.UseWhen(context => !context.Request.Path.StartsWithSegments("/admin/host/status"), config =>
            {
                config.UseMiddleware<ScriptHostCheckMiddleware>();
            });

            app.UseMvc(routes =>
            {
                routes.MapRoute(name: "Home",
                    template: string.Empty,
                    defaults: new { controller = "Home", action = "Get" });
            });
        }

        private IServiceProvider RegisterApplicationservices(IServiceCollection services)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebJobsScriptHostService>());

            var builder = new ContainerBuilder();

            builder.Populate(services);

            // TODO: This is a direct port from the current model.
            // Some of those services (or the way we register them) may need to change
            builder.RegisterType<DefaultSecretManagerFactory>().As<ISecretManagerFactory>().SingleInstance();
            builder.RegisterType<ScriptEventManager>().As<IScriptEventManager>().SingleInstance();
            builder.RegisterInstance(ScriptSettingsManager.Instance);
            builder.Register(c => WebHostSettings.CreateDefault(c.Resolve<ScriptSettingsManager>()));
            builder.RegisterType<WebHostResolver>().SingleInstance();

            // The services below need to be scoped to a pseudo-tenant (warm/specialized environment)
            builder.Register<WebScriptHostManager>(c => c.Resolve<WebHostResolver>().GetWebScriptHostManager()).ExternallyOwned();
            builder.Register<ISecretManager>(c => c.Resolve<WebHostResolver>().GetSecretManager()).ExternallyOwned();

            var applicationContainer = builder.Build();

            return new AutofacServiceProvider(applicationContainer);
        }
    }
}
