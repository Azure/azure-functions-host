// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Constraints;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;

using Microsoft.Extensions.Logging;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Host.Loggers;
using WebJobs.Script.WebHost.Core;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        // TODO: FACAVAL (WEBHOOKS SDK)
        // private static Lazy<MethodInfo> _getWebHookDataMethod = new Lazy<MethodInfo>(CreateGetWebHookDataMethodInfo);
        private static bool? _standbyMode;

        private readonly WebHostMetricsLogger _metricsLogger;
        private readonly ISecretManager _secretManager;
        private readonly HostPerformanceManager _performanceManager;
        private readonly WebHostSettings _webHostSettings;

        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly ScriptHostConfiguration _config;

        // TODO: FACAVAL
        //private readonly ISwaggerDocumentManager _swaggerDocumentManager;
        private readonly object _syncLock = new object();
        private readonly int _hostTimeoutSeconds;
        private readonly int _hostRunningPollIntervalMilliseconds;
        private readonly IWebJobsRouter _router;
        private readonly WebJobsSdkExtensionHookProvider _bindingWebHookProvider;

        private bool _warmupComplete = false;
        private bool _hostStarted = false;

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            IScriptHostFactory scriptHostFactory = null,
            ISecretsRepositoryFactory secretsRepositoryFactory = null,
            ILoggerFactoryBuilder loggerFactoryBuilder = null,
            int hostTimeoutSeconds = 30,
            int hostPollingIntervalMilliseconds = 500)
            : base(config, settingsManager, scriptHostFactory, eventManager, environment: null, loggerFactoryBuilder: loggerFactoryBuilder)
        {
            _config = config;

            _metricsLogger = new WebHostMetricsLogger();
            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _webHostSettings = webHostSettings;
            _hostTimeoutSeconds = hostTimeoutSeconds;
            _hostRunningPollIntervalMilliseconds = hostPollingIntervalMilliseconds;
            _router = router;

            var systemEventGenerator = config.HostConfig.GetService<IEventGenerator>() ?? new EventGenerator();
            var systemTraceWriter = new SystemTraceWriter(systemEventGenerator, settingsManager, TraceLevel.Verbose);
            if (config.TraceWriter != null)
            {
                config.TraceWriter = new CompositeTraceWriter(new TraceWriter[] { config.TraceWriter, systemTraceWriter });
            }
            else
            {
                config.TraceWriter = systemTraceWriter;
            }

            config.IsSelfHost = webHostSettings.IsSelfHost;

            _performanceManager = new HostPerformanceManager(settingsManager, config.TraceWriter);

            // TODO: FACAVAL
            // _swaggerDocumentManager = new SwaggerDocumentManager(config);

            var secretsRepository = secretsRepositoryFactory.Create(settingsManager, webHostSettings, config);
            _secretManager = secretManagerFactory.Create(settingsManager, config.HostConfig.LoggerFactory, secretsRepository);

            _bindingWebHookProvider = new WebJobsSdkExtensionHookProvider(_secretManager);
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            IScriptHostFactory scriptHostFactory,
            ILoggerFactoryBuilder loggerFactoryBuilder)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, router, scriptHostFactory, new DefaultSecretsRepositoryFactory(), loggerFactoryBuilder)
        {
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IWebJobsRouter router,
            ILoggerFactoryBuilder loggerFactoryBuilder)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, router, new ScriptHostFactory(), loggerFactoryBuilder)
        {
        }

        internal WebJobsSdkExtensionHookProvider BindingWebHookProvider => _bindingWebHookProvider;

        public ISecretManager SecretManager => _secretManager;

        public HostPerformanceManager PerformanceManager => _performanceManager;

        // TODO: FACAVAL
        // public ISwaggerDocumentManager SwaggerDocumentManager => _swaggerDocumentManager;

        public virtual bool Initialized
        {
            get
            {
                if (InStandbyMode)
                {
                    return _warmupComplete;
                }
                else
                {
                    return _hostStarted;
                }
            }
        }

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

        public void Initialize(CancellationToken cancellationToken)
        {
            lock (_syncLock)
            {
                if (InStandbyMode)
                {
                    if (!_warmupComplete)
                    {
                        WarmUp(_webHostSettings, EventManager);

                        _warmupComplete = true;
                    }
                }
                else if (!_hostStarted)
                {
                    RunAndBlock(cancellationToken);

                    _hostStarted = true;
                }
            }
        }

        public static void WarmUp(WebHostSettings settings, IScriptEventManager eventManager)
        {
            var traceWriter = new FileTraceWriter(Path.Combine(settings.LogPath, "Host"), TraceLevel.Info);
            ScriptHost host = null;
            try
            {
                traceWriter.Info("Warm up started");

                string rootPath = settings.ScriptPath;
                if (Directory.Exists(rootPath))
                {
                    Directory.Delete(rootPath, true);
                }
                Directory.CreateDirectory(rootPath);

                string content = ReadResourceString("Functions.host.json");
                File.WriteAllText(Path.Combine(rootPath, "host.json"), content);

                // read in the C# function
                string functionPath = Path.Combine(rootPath, "Test-CSharp");
                Directory.CreateDirectory(functionPath);
                content = ReadResourceString("Functions.Test_CSharp.function.json");
                File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
                content = ReadResourceString("Functions.Test_CSharp.run.csx");
                File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

                traceWriter.Info("Warm up functions deployed");

                ScriptHostConfiguration config = new ScriptHostConfiguration
                {
                    RootScriptPath = rootPath,
                    FileLoggingMode = FileLoggingMode.Never,
                    RootLogPath = settings.LogPath,
                    TraceWriter = traceWriter,
                    FileWatchingEnabled = false
                };
                config.HostConfig.StorageConnectionString = null;
                config.HostConfig.DashboardConnectionString = null;

                host = ScriptHost.Create(new NullScriptHostEnvironment(), eventManager, config, ScriptSettingsManager.Instance);
                traceWriter.Info(string.Format("Starting Host (Id={0})", host.ScriptConfig.HostConfig.HostId));

                host.Start();

                var arguments = new Dictionary<string, object>
                {
                    { "input", "{}" }
                };
                host.CallAsync("Test-CSharp", arguments).Wait();
                host.Stop();
#if NODE
                await NodeFunctionInvoker.InitializeAsync();
#endif
                traceWriter.Info("Warm up succeeded");
            }
            catch (Exception ex)
            {
                traceWriter.Error(string.Format("Warm up failed: {0}", ex));
            }
            finally
            {
                host?.Dispose();
                traceWriter.Dispose();
            }
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

        private static string ReadResourceString(string fileName)
        {
            string resourcePath = string.Format("Microsoft.Azure.WebJobs.Script.WebHost.Resources.{0}", fileName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(resourcePath)))
            {
                return reader.ReadToEnd();
            }
        }

        // TODO: FACAVAL (WEBHOOKS SDK)
        // private static MethodInfo CreateGetWebHookDataMethodInfo()
        // {
        //     return typeof(WebHookHandlerContextExtensions).GetMethod("GetDataOrDefault", BindingFlags.Public | BindingFlags.Static);
        // }

        // TODO: FACAVAL (WEBHOOKS SDK)
        // private static object GetWebHookData(Type dataType, WebHookHandlerContext context)
        // {
        //     MethodInfo getDataMethod = _getWebHookDataMethod.Value.MakeGenericMethod(dataType);
        //     return getDataMethod.Invoke(null, new object[] { context });
        // }

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

            // disable standard Dashboard logging (enabling Table logging below)
            hostConfig.DashboardConnectionString = null;

            // HostId may be missing in local test scenarios.
            var hostId = hostConfig.HostId ?? "default";
            Func<string, FunctionDescriptor> funcLookup = (name) => this.Instance.GetFunctionOrNull(name);
            var loggingConnectionString = config.HostConfig.DashboardConnectionString;
            var instanceLogger = new FunctionInstanceLogger(funcLookup, _metricsLogger, hostId, loggingConnectionString, config.TraceWriter);
            hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(instanceLogger);
        }

        protected override void OnHostCreated()
        {
            InitializeHttp();

            base.OnHostCreated();
        }

        protected override void OnHostStarted()
        {
            // Purge any old Function secrets
            _secretManager.PurgeOldSecretsAsync(Instance.ScriptConfig.RootScriptPath, Instance.TraceWriter, Instance.Logger);

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
            WebJobsRouteBuilder routesBuilder = _router.CreateBuilder(new ScriptRouteHandler(loggerFactory, () => Instance), httpConfig.RoutePrefix);

            // Proxies do not honor the route prefix defined in host.json
            WebJobsRouteBuilder proxiesRoutesBuilder = _router.CreateBuilder(new ScriptRouteHandler(loggerFactory, () => Instance), routePrefix: null);

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
            Instance?.TraceWriter.Info(message);
            Instance?.Logger?.LogInformation(message);

            Stop();

            Program.InitiateShutdown();
        }
    }
}