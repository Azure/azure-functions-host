// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Program
    {
        private static CancellationTokenSource _applicationCts = new CancellationTokenSource();

        public static void Main(string[] args)
        {
            BuildWebHost(args)
                .RunAsync(_applicationCts.Token)
                .Wait();
        }

        public static IWebHost BuildWebHost(string[] args)
        {
            return CreateWebHostBuilder(args).UseIIS().Build();
        }

        public static IWebHostBuilder CreateWebHostBuilder(string[] args = null)
        {
            return Microsoft.AspNetCore.WebHost.CreateDefaultBuilder(args)
                .ConfigureServices(services =>
                {
                   services.Replace(ServiceDescriptor.Singleton<IServiceProviderFactory<IServiceCollection>>(new FunctionsServiceProviderFactory()));
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
                    config.Add(new WebScriptHostConfigurationSource());
                })
                .ConfigureLogging(b => b.ClearProviders())
                .UseStartup<Startup>();
        }

        internal static void InitiateShutdown()
        {
            _applicationCts.Cancel();
        }
    }
}