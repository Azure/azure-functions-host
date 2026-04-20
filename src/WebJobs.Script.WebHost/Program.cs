// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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

        public static IHostBuilder CreateHostBuilder(string[] args = null)
        {
            // Setting this env variable to test placeholder scenarios locally.
#if PLACEHOLDER_SIMULATION
            SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode, "1");
            SystemEnvironment.Instance.SetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteContainerReady, "0");
#endif

            args ??= Array.Empty<string>();

            // Build a minimal IHostBuilder that registers only the configuration and logging
            // defaults the Functions host actually uses, equivalent to what the legacy
            // WebHost.CreateDefaultBuilder provided. Host.CreateDefaultBuilder adds additional
            // defaults (metrics, host option bindings, startup validation) that the Functions
            // host does not need and that inflate startup cost.
            return new HostBuilder()
                .UseContentRoot(Environment.CurrentDirectory)
                .ConfigureAppConfiguration((hostingContext, config) =>
                {
                    IHostEnvironment env = hostingContext.HostingEnvironment;
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                    // Match net8 WebHost.CreateDefaultBuilder: load user secrets in Development
                    // so devs can `dotnet user-secrets set` against the WebHost's UserSecretsId
                    // (see WebJobs.Script.WebHost.csproj). No-op in Production.
                    if (env.IsDevelopment())
                    {
                        config.AddUserSecrets<Program>(optional: true);
                    }

                    config.AddEnvironmentVariables();
                    if (args.Length > 0)
                    {
                        config.AddCommandLine(args);
                    }
                })
                // Scope and build validation are intentionally disabled. The host uses a two-level
                // DI hierarchy with cross-boundary service resolution that would fail generic scope
                // validation. The custom DependencyValidator provides tighter, bespoke validation.
                // This preserves the behavior of WebHost.CreateDefaultBuilder(), which also disabled
                // scope validation in Production by default.
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
                            // replace the default environment source with our own
                            IConfigurationSource envVarsSource = config.Sources.OfType<EnvironmentVariablesConfigurationSource>().FirstOrDefault();
                            if (envVarsSource != null)
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

                            var hostingEnvironmentConfigFilePath = SystemEnvironment.Instance.GetFunctionsHostingEnvironmentConfigFilePath();
                            if (!string.IsNullOrEmpty(hostingEnvironmentConfigFilePath))
                            {
                                config.AddJsonFile(hostingEnvironmentConfigFilePath, optional: true, reloadOnChange: false);
                            }
                        })
                        .ConfigureLogging((context, loggingBuilder) =>
                        {
                            // Match the net8 WebHost.CreateDefaultBuilder baseline: bind log filter
                            // levels from the "Logging" configuration section and enable activity
                            // tracking for scopes. ClearProviders() below only clears ILoggerProvider
                            // registrations, so these option bindings are preserved.
                            loggingBuilder.Configure(options =>
                            {
                                options.ActivityTrackingOptions = ActivityTrackingOptions.SpanId
                                    | ActivityTrackingOptions.TraceId
                                    | ActivityTrackingOptions.ParentId;
                            });
                            loggingBuilder.AddConfiguration(context.Configuration.GetSection("Logging"));

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
                })
                // Register command-line args on host configuration AFTER ConfigureWebHostDefaults
                // so the cmdline provider is the last source added. Host-level settings such as
                // --urls, --environment, and --contentRoot then win over any ASPNETCORE_-prefixed
                // environment variables, matching the net8 WebHost.CreateDefaultBuilder priority
                // (which copied cmdline values into the web host via UseSetting).
                .ConfigureHostConfiguration(config =>
                {
                    if (args.Length > 0)
                    {
                        config.AddCommandLine(args);
                    }
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
