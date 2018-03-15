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
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Azure.WebJobs.Script.Scale;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private static bool? _standbyMode;

        private readonly WebHostMetricsLogger _metricsLogger;
        private readonly ISecretManager _secretManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly ScriptSettingsManager _settingsManager;
        private readonly IFunctionMonitor _functionMonitor;

        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly ScriptHostConfiguration _config;

        private readonly object _syncLock = new object();
        private readonly int _hostTimeoutSeconds;
        private readonly int _hostRunningPollIntervalMilliseconds;
        private readonly IWebJobsRouter _router;
        private readonly WebJobsSdkExtensionHookProvider _bindingWebHookProvider;

        private Task _runTask = Task.CompletedTask;
        private bool _hostStarted = false;

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            ILoggerFactory loggerFactory,
            IScriptHostFactory scriptHostFactory = null,
            ISecretsRepositoryFactory secretsRepositoryFactory = null,
            HostPerformanceManager hostPerformanceManager = null,
            ILoggerProviderFactory loggerProviderFactory = null,
            IEventGenerator eventGenerator = null,
            IFunctionMonitor functionMonitor = null,
            int hostTimeoutSeconds = 30,
            int hostPollingIntervalMilliseconds = 500)
            : base(config, settingsManager, scriptHostFactory, eventManager, environment: null,
                  hostPerformanceManager: hostPerformanceManager, loggerProviderFactory: loggerProviderFactory)
        {
            _config = config;

            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _webHostSettings = webHostSettings;
            _settingsManager = settingsManager;
            _hostTimeoutSeconds = hostTimeoutSeconds;
            _hostRunningPollIntervalMilliseconds = hostPollingIntervalMilliseconds;
            _router = router;
            _functionMonitor = functionMonitor;

            config.IsSelfHost = webHostSettings.IsSelfHost;

            secretsRepositoryFactory = secretsRepositoryFactory ?? new DefaultSecretsRepositoryFactory();
            var secretsRepository = secretsRepositoryFactory.Create(settingsManager, webHostSettings, config);
            _secretManager = secretManagerFactory.Create(settingsManager, loggerFactory.CreateLogger(ScriptConstants.LogCategoryHostGeneral), secretsRepository);
            eventGenerator = eventGenerator ?? new EtwEventGenerator();

            _bindingWebHookProvider = new WebJobsSdkExtensionHookProvider(_secretManager);
            _metricsLogger = new WebHostMetricsLogger(eventGenerator);
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            ILoggerFactory loggerFactory,
            IScriptHostFactory scriptHostFactory)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, router, loggerFactory, scriptHostFactory, new DefaultSecretsRepositoryFactory())
        {
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            ILoggerFactory loggerFactory)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, router, loggerFactory, new ScriptHostFactory())
        {
        }

        internal WebJobsSdkExtensionHookProvider BindingWebHookProvider => _bindingWebHookProvider;

        public ISecretManager SecretManager => _secretManager;

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
        public Task RunAsync(CancellationToken cancellationToken)
        {
            lock (_syncLock)
            {
                if (!_hostStarted)
                {
                    _runTask = Task.Run(() => RunAndBlock(cancellationToken));

                    _functionMonitor.Start();

                    _hostStarted = true;
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

        protected override void OnInitializeConfig(ScriptHostConfiguration config)
        {
            base.OnInitializeConfig(config);

            // Note: this method can be called many times for the same ScriptHostConfiguration
            // so no changes should be made to the configuration itself. It is safe to modify
            // ScriptHostConfiguration.Host config though, since the inner JobHostConfiguration
            // is created on each restart.

            // Add our WebHost specific services
            var hostConfig = config.HostConfig;

            hostConfig.AddService<IMetricsLogger>(_metricsLogger);
            hostConfig.AddService<IWebHookProvider>(_bindingWebHookProvider);

            // Add our exception handler
            hostConfig.AddService<IWebJobsExceptionHandler>(_exceptionHandler);

            // HostId may be missing in local test scenarios.
            var hostId = hostConfig.HostId ?? "default";
            Func<string, FunctionDescriptor> funcLookup = (name) => this.Instance.GetFunctionOrNull(name);
            var loggingConnectionString = config.HostConfig.DashboardConnectionString;

            // TODO: This is asking for a LoggerFactory before the LoggerFactory is ready. Pass a Null instance for now.
            var instanceLogger = new FunctionInstanceLogger(funcLookup, _metricsLogger, hostId, loggingConnectionString, NullLoggerFactory.Instance);
            hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(instanceLogger);

            // disable standard Dashboard logging (enabling Table logging above)
            hostConfig.DashboardConnectionString = null;
        }

        protected override void OnHostInitialized()
        {
            if (InStandbyMode)
            {
                Instance?.Logger.LogInformation("Host is in standby mode");
            }

            InitializeHttp();

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

        private void InitializeHttp()
        {
            // get the registered http configuration from the extension registry
            var extensions = Instance.ScriptConfig.HostConfig.GetService<IExtensionRegistry>();
            HttpExtensionConfiguration httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            InitializeHttpFunctions(Instance.Functions, httpConfig);
        }

        private void InitializeHttpFunctions(IEnumerable<FunctionDescriptor> functions, HttpExtensionConfiguration httpConfig)
        {
            _router.ClearRoutes();

            // TODO: FACAVAL Instantiation of the ScriptRouteHandler should be cleaned up
            ILoggerFactory loggerFactory = _config.HostConfig.LoggerFactory;
            WebJobsRouteBuilder routesBuilder = _router.CreateBuilder(new ScriptRouteHandler(loggerFactory, this, _settingsManager), httpConfig.RoutePrefix);

            // Proxies do not honor the route prefix defined in host.json
            WebJobsRouteBuilder proxiesRoutesBuilder = _router.CreateBuilder(new ScriptRouteHandler(loggerFactory, this, _settingsManager), routePrefix: null);

            foreach (var function in functions)
            {
                var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
                if (httpTrigger != null)
                {
                    var constraints = new RouteValueDictionary();
                    if (httpTrigger.Methods != null)
                    {
                        constraints.Add("httpMethod", new HttpMethodRouteConstraint(httpTrigger.Methods));
                    }

                    string route = httpTrigger.Route;

                    if (string.IsNullOrEmpty(route))
                    {
                        route = function.Name;
                    }

                    WebJobsRouteBuilder builder = function.Metadata.IsProxy ? proxiesRoutesBuilder : routesBuilder;
                    builder.MapFunctionRoute(function.Metadata.Name, route, constraints, function.Metadata.Name);
                }
            }

            // Proxy routes will take precedence over http trigger functions
            // so they will be added first to the router.
            if (proxiesRoutesBuilder.Count > 0)
            {
                _router.AddFunctionRoute(proxiesRoutesBuilder.Build());
            }

            if (routesBuilder.Count > 0)
            {
                _router.AddFunctionRoute(routesBuilder.Build());
            }
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
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            TimeSpan timeWaited = TimeSpan.Zero;

            while (!hostManager.CanInvoke() &&
                    hostManager.State != ScriptHostState.Error &&
                    (timeWaited < timeout))
            {
                await Task.Delay(delay);
                timeWaited += delay;
            }

            bool hostReady = hostManager.CanInvoke();

            if (throwOnFailure && !hostReady)
            {
                throw new HttpException(HttpStatusCode.ServiceUnavailable, "Function host is not running.");
            }

            return hostReady;
        }
    }
}