// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using DataProtectionConstants = Microsoft.Azure.Web.DataProtection.Constants;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class Program
    {
        public static void Main(string[] args)
        {
            InitializeProcess();

            var host = BuildHost(args);

            host.RunAsync()
                .Wait();
        }

        public static IHost BuildHost(string[] args)
        {
            return CreateHostBuilder(args)
                .Build();
        }

        /// <summary>
        /// Creates an <see cref="IHostBuilder"/> with only the services the Functions host requires.
        /// </summary>
        /// <remarks>
        /// A bare <see cref="HostBuilder"/> is used instead of <c>Host.CreateDefaultBuilder</c>
        /// because the Functions host manages its own logging, configuration, and metrics.
        /// </remarks>
        public static IHostBuilder CreateHostBuilder(string[] args = null)
        {
#if PLACEHOLDER_SIMULATION
            SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "0");
#endif

            return new HostBuilder()
                .UseContentRoot(Directory.GetCurrentDirectory())
                .ConfigureHostConfiguration(config =>
                {
                    if (args is { Length: > 0 })
                    {
                        config.AddCommandLine(args);
                    }
                })
                // Scope and build validation are disabled because the host uses a two-level
                // DI hierarchy with cross-boundary service resolution. The custom
                // DependencyValidator provides bespoke validation instead.
                .UseDefaultServiceProvider((context, options) =>
                {
                    options.ValidateScopes = false;
                    options.ValidateOnBuild = false;
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .ConfigureKestrel(o =>
                        {
                            o.Limits.MaxRequestBodySize = ScriptConstants.DefaultMaxRequestBodySize;
                        })
                        .UseSetting(WebHostDefaults.EnvironmentKey, Environment.GetEnvironmentVariable(EnvironmentSettingNames.EnvironmentNameKey))
                        .ConfigureServices(services =>
                        {
                            services.Configure<IISServerOptions>(o =>
                            {
                                o.MaxRequestBodySize = ScriptConstants.DefaultMaxRequestBodySize;
                            });
                        })
                        .ConfigureAppConfiguration((builderContext, config) =>
                        {
                            // Replace the default environment variables source with one
                            // that is aware of Functions-specific settings.
                            IConfigurationSource envVarsSource = config.Sources.OfType<EnvironmentVariablesConfigurationSource>().FirstOrDefault();
                            if (envVarsSource is not null)
                            {
                                config.Sources.Remove(envVarsSource);
                            }

                            config.Add(new ScriptEnvironmentVariablesConfigurationSource());

                            config.Add(new WebScriptHostConfigurationSource
                            {
                                IsAppServiceEnvironment = SystemEnvironment.Instance.IsAppService(),
                                IsLinuxContainerEnvironment = SystemEnvironment.Instance.IsAnyLinuxConsumption(),
                                IsLinuxAppServiceEnvironment = SystemEnvironment.Instance.IsLinuxAppService()
                            });
                            config.Add(new FunctionsHostingConfigSource(SystemEnvironment.Instance));

                            if (builderContext.HostingEnvironment.IsDevelopment())
                            {
                                config.AddUserSecrets<Program>(optional: true);
                            }

                            var hostingEnvironmentConfigFilePath = SystemEnvironment.Instance.GetFunctionsHostingEnvironmentConfigFilePath();
                            if (!string.IsNullOrEmpty(hostingEnvironmentConfigFilePath))
                            {
                                config.AddJsonFile(hostingEnvironmentConfigFilePath, optional: true, reloadOnChange: false);
                            }
                        })
                        .ConfigureLogging((context, loggingBuilder) =>
                        {
                            loggingBuilder.Configure(options =>
                            {
                                options.ActivityTrackingOptions =
                                    ActivityTrackingOptions.SpanId |
                                    ActivityTrackingOptions.TraceId |
                                    ActivityTrackingOptions.ParentId;
                            });

                            loggingBuilder.ClearProviders();

                            loggingBuilder.AddDefaultWebJobsFilters();
                            loggingBuilder.AddWebJobsSystem<WebHostSystemLoggerProvider>();
                            loggingBuilder.AddForwardingLogger();
                            loggingBuilder.Services.AddSingleton<DeferredLoggerProvider>();
                            loggingBuilder.Services.AddSingleton<ILoggerProvider>(s => s.GetRequiredService<DeferredLoggerProvider>());
                            loggingBuilder.Services.AddSingleton<ISystemLoggerFactory, SystemLoggerFactory>();
                            if (context.HostingEnvironment.IsDevelopment())
                            {
                                loggingBuilder.AddConsole();
                            }
                        })
                        .UseStartup<Startup>()
                        .UseIIS();
                });
        }

        /// <summary>
        /// Perform any process level initialization that needs to happen BEFORE
        /// the WebHost is initialized.
        /// </summary>
        private static void InitializeProcess()
        {
            if (SystemEnvironment.Instance.IsLinuxConsumptionOnAtlas())
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledExceptionInLinuxConsumption;
            }
            else if (SystemEnvironment.Instance.IsFlexConsumptionSku() ||
                SystemEnvironment.Instance.IsLinuxConsumptionOnLegion())
            {
                // TODO: Replace with legion specific logger?
                AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledExceptionInLinuxConsumption;
            }
            else if (SystemEnvironment.Instance.IsLinuxAppService())
            {
                AppDomain.CurrentDomain.UnhandledException += CurrentDomainOnUnhandledExceptionInLinuxAppService;
            }

            // Some environments only set the auth key. Ensure that is used as the encryption key if that is not set
            string authEncryptionKey = SystemEnvironment.Instance.GetEnvironmentVariable(EnvironmentSettingNames.WebSiteAuthEncryptionKey);
            if (authEncryptionKey != null &&
                SystemEnvironment.Instance.GetEnvironmentVariable(DataProtectionConstants.AzureWebsiteEnvironmentMachineKey) == null)
            {
                SystemEnvironment.Instance.SetEnvironmentVariable(DataProtectionConstants.AzureWebsiteEnvironmentMachineKey, authEncryptionKey);
            }

            ConfigureMinimumThreads(SystemEnvironment.Instance);
        }

        private static void CurrentDomainOnUnhandledExceptionInLinuxConsumption(object sender, UnhandledExceptionEventArgs e)
        {
            // Fallback console logs in case kusto logging fails.
            Console.WriteLine($"{nameof(CurrentDomainOnUnhandledExceptionInLinuxConsumption)}: {e.ExceptionObject}");

            LinuxContainerEventGenerator.LogUnhandledException((Exception)e.ExceptionObject);
        }

        private static void CurrentDomainOnUnhandledExceptionInLinuxAppService(object sender, UnhandledExceptionEventArgs e)
        {
            LinuxAppServiceEventGenerator.LogUnhandledException((Exception)e.ExceptionObject);
        }

        private static void ConfigureMinimumThreads(IEnvironment environment)
        {
            // For information on MinThreads, see:
            // https://docs.microsoft.com/en-us/dotnet/api/system.threading.threadpool.setminthreads?view=netcore-2.2
            // https://docs.microsoft.com/en-us/azure/redis-cache/cache-faq#important-details-about-threadpool-growth
            // https://blogs.msdn.microsoft.com/perfworld/2010/01/13/how-can-i-improve-the-performance-of-asp-net-by-adjusting-the-clr-thread-throttling-properties/
            //
            // This behavior can be overridden by using the "ComPlus_ThreadPool_ForceMinWorkerThreads" environment variable (honored by the .NET threadpool).

            var effectiveCores = environment.GetEffectiveCoresCount();

            // This value was derived by looking at the thread count for several function apps running load on a multicore machine and dividing by the number of cores.
            const int minThreadsPerLogicalProcessor = 6;

            int minThreadCount = effectiveCores * minThreadsPerLogicalProcessor;
            ThreadPool.SetMinThreads(minThreadCount, minThreadCount);
        }
    }
}
