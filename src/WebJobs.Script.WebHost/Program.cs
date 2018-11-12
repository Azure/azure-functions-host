// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Abstractions;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Grpc;
using Microsoft.Azure.WebJobs.Script.Rpc;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var host = BuildWebHost(args);

            var environment = host.Services.GetService<IEnvironment>();
            if (environment.IsLinuxContainerEnvironment())
            {
                // Linux containers always start out in placeholder mode
                environment.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }

            host.RunAsync()
                .Wait();
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            return CreateWebHostBuilder(args).UseIIS().Build();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args = null)
        {
            return AspNetCore.WebHost.CreateDefaultBuilder(args)
                .UseKestrel(o =>
                {
                    o.Limits.MaxRequestBodySize = 104857600;
                })
                .UseSetting(WebHostDefaults.EnvironmentKey, Environment.GetEnvironmentVariable(EnvironmentSettingNames.EnvironmentNameKey))
                .ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new WebHostServiceProviderFactory()));
                    services.TryAddSingleton<IScriptEventManager, ScriptEventManager>();
                })
                .ConfigureAppConfiguration((builderContext, config) =>
                {
                    // replace the default environment source with our own
                    IConfigurationSource envVarsSource = config.Sources.OfType<EnvironmentVariablesConfigurationSource>().FirstOrDefault();
                    if (envVarsSource != null)
                    {
                        config.Sources.Remove(envVarsSource);
                    }

                    config.Add(new ScriptEnvironmentVariablesConfigurationSource());

                    config.Add(new WebScriptHostConfigurationSource
                    {
                        IsAppServiceEnvironment = SystemEnvironment.Instance.IsAppServiceEnvironment(),
                        IsLinuxContainerEnvironment = SystemEnvironment.Instance.IsLinuxContainerEnvironment(),
                        IsLinuxAppServiceEnvironment = SystemEnvironment.Instance.IsLinuxAppServiceEnvironment()
                    });
                })
                .ConfigureLogging((context, loggingBuilder) =>
                {
                    loggingBuilder.ClearProviders();
                    loggingBuilder.AddConsole();
                    loggingBuilder.AddDefaultWebJobsFilters();
                    loggingBuilder.AddWebJobsSystem<WebHostSystemLoggerProvider>();
                })
                .UseStartup<Startup>();
        }
    }
}