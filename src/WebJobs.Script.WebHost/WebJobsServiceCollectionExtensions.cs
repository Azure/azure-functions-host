using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace WebJobs.Script.WebHost.Core
{
    public static class WebJobsServiceCollectionExtensions
    {
        public static IServiceCollection AddWebJobsScriptHost(this IServiceCollection services)
        {
            RegisterWebJobsScriptServices(services);

            return services;
        }

        private static void RegisterWebJobsScriptServices(IServiceCollection services)
        {
            // TODO: This is a direct port from the current model.
            // Some of those services (or the way we register them) may need to change
            services.TryAddSingleton<ISecretManagerFactory, DefaultSecretManagerFactory>();
            services.TryAddSingleton<IScriptEventManager, ScriptEventManager>();
            services.TryAddSingleton<ScriptSettingsManager>(ScriptSettingsManager.Instance);
            services.TryAddSingleton<WebHostSettings>(c => WebHostSettings.CreateDefault(c.GetService<ScriptSettingsManager>()));
            services.TryAddSingleton<WebHostResolver>();
            services.TryAddSingleton<WebScriptHostManager>(c => c.GetService<WebHostResolver>().GetWebScriptHostManager(c.GetService<WebHostSettings>()));
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebJobsScriptHostService>());
        }
    }
}
