// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using DataProtectionCostants = Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Program
    {
        public static void Main(string[] args)
        {
            InitializeProcess();

            var host = BuildWebHost(args);

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
                .ConfigureKestrel(o =>
                {
                    o.Limits.MaxRequestBodySize = 104857600;
                })
                .UseSetting(WebHostDefaults.EnvironmentKey, Environment.GetEnvironmentVariable(EnvironmentSettingNames.EnvironmentNameKey))
                .ConfigureServices(services =>
                {
                    services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new WebHostServiceProviderFactory()));
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

                    loggingBuilder.AddDefaultWebJobsFilters();
                    loggingBuilder.AddWebJobsSystem<WebHostSystemLoggerProvider>();
                    loggingBuilder.AddDeferred();
                })
                .UseStartup<Startup>();
        }

        /// <summary>
        /// Perform any process level initialization that needs to happen BEFORE
        /// the WebHost is initialized.
        /// </summary>
        private static void InitializeProcess()
        {
            if (SystemEnvironment.Instance.IsLinuxContainerEnvironment())
            {
                // Linux containers always start out in placeholder mode
                SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            }

            // Some environments only set the auth key. Ensure that is used as the encryption key if that is not set
            string authEncryptionKey = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
            if (authEncryptionKey != null &&
                SystemEnvironment.Instance.GetEnvironmentVariable(DataProtectionCostants.AzureWebsiteEnvironmentMachineKey) == null)
            {
                SystemEnvironment.Instance.SetEnvironmentVariable(DataProtectionCostants.AzureWebsiteEnvironmentMachineKey, authEncryptionKey);
            }
        }
    }
}