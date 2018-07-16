// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private static bool? _standbyMode;

        private readonly WebHostMetricsLogger _metricsLogger;
        private readonly ISecretManager _secretManager;
        private readonly ScriptWebHostOptions _webHostSettings;
        private readonly ScriptSettingsManager _settingsManager;

        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly ScriptHostOptions _config;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IExtensionRegistry _extensionRegistry;
        private readonly object _syncLock = new object();
        private readonly int _hostTimeoutSeconds;
        private readonly int _hostRunningPollIntervalMilliseconds;
        private readonly WebJobsSdkExtensionHookProvider _bindingWebHookProvider;

        private Task _runTask = Task.CompletedTask;
        private bool _hostStarted = false;

        public WebScriptHostManager(ScriptHostOptions config,
            IOptions<JobHostOptions> jobHostOptions,
            IMetricsLogger metricsLogger,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            ScriptWebHostOptions webHostSettings,
            IWebJobsRouter router,
            ILoggerFactory loggerFactory,
            IConnectionStringProvider connectionStringProvider,
            IScriptHostFactory scriptHostFactory,
            IExtensionRegistry extensionRegistry,
            ISecretsRepositoryFactory secretsRepositoryFactory,
            HostPerformanceManager hostPerformanceManager = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            IEventGenerator eventGenerator = null,
            int hostTimeoutSeconds = 30,
            int hostPollingIntervalMilliseconds = 500)
            : base(config,
                  jobHostOptions,
                  settingsManager,
                  metricsLogger,
                  scriptHostFactory,
                  eventManager,
                  environment: null,
                  hostPerformanceManager: hostPerformanceManager,
                  loggerProviderFactory: loggerProviderFactory)
        {
            _config = config;
            _loggerFactory = loggerFactory;
            _extensionRegistry = extensionRegistry;
            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _webHostSettings = webHostSettings;
            _settingsManager = settingsManager;
            _hostTimeoutSeconds = hostTimeoutSeconds;
            _hostRunningPollIntervalMilliseconds = hostPollingIntervalMilliseconds;

            config.IsSelfHost = webHostSettings.IsSelfHost;

            secretsRepositoryFactory = secretsRepositoryFactory ?? new DefaultSecretsRepositoryFactory();
            var secretsRepository = secretsRepositoryFactory.Create(settingsManager, webHostSettings, config, connectionStringProvider);
            _secretManager = secretManagerFactory.Create();
            eventGenerator = eventGenerator ?? new EtwEventGenerator();

            _bindingWebHookProvider = new WebJobsSdkExtensionHookProvider(_secretManager);
            _metricsLogger = new WebHostMetricsLogger(eventGenerator);
        }

        public WebScriptHostManager(ScriptHostOptions config,
            IOptions<JobHostOptions> jobHostOptions,
            IMetricsLogger metricsLogger,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            ScriptWebHostOptions webHostSettings,
            IWebJobsRouter router,
            ILoggerFactory loggerFactory,
            IConnectionStringProvider connectionStringProvider,
            IScriptHostFactory scriptHostFactory,
            IExtensionRegistry extensionRegistry)
            : this(config,
                  jobHostOptions,
                  metricsLogger,
                  secretManagerFactory,
                  eventManager,
                  settingsManager,
                  webHostSettings,
                  router,
                  loggerFactory,
                  connectionStringProvider,
                  scriptHostFactory,
                  extensionRegistry,
                  new DefaultSecretsRepositoryFactory())
        {
        }

        internal WebJobsSdkExtensionHookProvider BindingWebHookProvider => _bindingWebHookProvider;

        public ISecretManager SecretManager => _secretManager;

        /// <summary>
        /// Gets or sets a value indicating whether http requests should be
        /// temporarily delayed due to host state.
        /// </summary>
        public static bool DelayRequests { get; set; }

        public static bool InStandbyMode
        {
            get
            {
                // once set, never reset
                if (_standbyMode != null)
                {
                    return _standbyMode.Value;
                }
                if (ScriptSettingsManager.Instance.GetSetting(EnvironmentSettingNames.AzureWebsitePlaceholderMode) == "1")
                {
                    return true;
                }

                // no longer standby mode
                _standbyMode = false;

                return _standbyMode.Value;
            }
        }

        /// <summary>
        /// Ensures that the host has been fully initialized and startup
        /// has been initiated. This method is idempotent.
        /// </summary>
        public Task EnsureHostStarted(CancellationToken cancellationToken)
        {
            if (!_hostStarted)
            {
                lock (_syncLock)
                {
                    if (!_hostStarted)
                    {
                        _runTask = Task.Run(() => RunAndBlock(cancellationToken));
                        _hostStarted = true;
                    }
                }
            }

            return _runTask;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_secretManager as IDisposable)?.Dispose();

                _metricsLogger?.Dispose();
            }

            base.Dispose(disposing);
        }

        // this is for testing only
        internal static void ResetStandbyMode()
        {
            _standbyMode = null;
        }

        //protected override void OnInitializeConfig(ScriptHostOptions config)
        //{
            //base.OnInitializeConfig(config);

            // TODO: DI (FACAVAL) Move all service registration and configuration
            // Note: this method can be called many times for the same ScriptHostConfiguration
            // so no changes should be made to the configuration itself. It is safe to modify
            // ScriptHostConfiguration.Host config though, since the inner JobHostConfiguration
            // is created on each restart.

            // Add our WebHost specific services
            //var hostConfig = config.HostConfig;

            //hostConfig.AddService<IMetricsLogger>(_metricsLogger);
            //hostConfig.AddService<IWebHookProvider>(_bindingWebHookProvider);

            //// Add our exception handler
            //hostConfig.AddService<IWebJobsExceptionHandler>(_exceptionHandler);

            // HostId may be missing in local test scenarios.
            //var hostId = hostConfig.HostId ?? "default";
            //Func<string, FunctionDescriptor> funcLookup = (name) => this.Instance.GetFunctionOrNull(name);
            //var loggingConnectionString = config.HostConfig.DashboardConnectionString;

            // TODO: This is asking for a LoggerFactory before the LoggerFactory is ready. Pass a Null instance for now.
            //var instanceLogger = new FunctionInstanceLogger(funcLookup, _metricsLogger, hostId, loggingConnectionString, NullLoggerFactory.Instance);
            //hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(instanceLogger);

            //// disable standard Dashboard logging (enabling Table logging above)
            //hostConfig.DashboardConnectionString = null;
        //}

        protected override void OnHostInitialized()
        {
            if (InStandbyMode)
            {
                Instance?.Logger.LogInformation("Host is in standby mode");
            }

            base.OnHostInitialized();
        }

        protected override void OnHostStarted()
        {
            if (!InStandbyMode)
            {
                // Purge any old Function secrets
                _secretManager.PurgeOldSecretsAsync(Instance.ScriptConfig.RootScriptPath, Instance.Logger);
            }

            base.OnHostStarted();
        }

        public override void Shutdown()
        {
            string message = "Environment shutdown has been triggered. Stopping host and signaling shutdown.";
            Instance?.Logger.LogInformation(message);

            Stop();

            Program.InitiateShutdown();
        }

        public Task DelayUntilHostReady()
        {
            // ensure that the host is ready to process requests
            return DelayUntilHostReady(_hostTimeoutSeconds, _hostRunningPollIntervalMilliseconds);
        }

        public Task<bool> DelayUntilHostReady(int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds, bool throwOnFailure = true)
        {
            return DelayUntilHostReady(this, timeoutSeconds, pollingIntervalMilliseconds, throwOnFailure);
        }

        internal static async Task<bool> DelayUntilHostReady(ScriptHostManager hostManager, int timeoutSeconds = ScriptConstants.HostTimeoutSeconds, int pollingIntervalMilliseconds = ScriptConstants.HostPollingIntervalMilliseconds, bool throwOnFailure = true)
        {
            await Utility.DelayAsync(timeoutSeconds, pollingIntervalMilliseconds, () =>
            {
                return !hostManager.CanInvoke() &&
                        hostManager.State != ScriptHostState.Error;
            });

            bool hostReady = hostManager.CanInvoke();

            if (throwOnFailure && !hostReady)
            {
                throw new HttpException(HttpStatusCode.ServiceUnavailable, "Function host is not running.");
            }

            return hostReady;
        }

        internal static async Task<bool> DelayUntilHostReady(WebHostResolver resolver)
        {
            // need to ensure the host startup has been initiated
            var manager = resolver.GetWebScriptHostManager();
            var tIgnore = manager.EnsureHostStarted(CancellationToken.None);

            // wait for the host to get into a running state
            return await manager.DelayUntilHostReady(throwOnFailure: false);
        }

        internal static async Task DelayUntilEnvironmentReady()
        {
            if (DelayRequests)
            {
                // delay until we're ready to start processing requests
                await Utility.DelayAsync(ScriptConstants.HostTimeoutSeconds, ScriptConstants.HostPollingIntervalMilliseconds, () =>
                {
                    return DelayRequests;
                });
            }
        }
    }
}