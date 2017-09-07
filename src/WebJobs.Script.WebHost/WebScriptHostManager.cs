// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Hosting;
using System.Web.Http;
using System.Web.Http.Routing;
using Microsoft.AspNet.WebHooks;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Config;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Eventing;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Handlers;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private static Lazy<MethodInfo> _getWebHookDataMethod = new Lazy<MethodInfo>(CreateGetWebHookDataMethodInfo);
        private static bool? _standbyMode;
        private readonly WebHostMetricsLogger _metricsLogger;
        private readonly ISecretManager _secretManager;
        private readonly HostPerformanceManager _performanceManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly ScriptHostConfiguration _config;
        private readonly ISwaggerDocumentManager _swaggerDocumentManager;
        private readonly object _syncLock = new object();
        private readonly int _hostTimeoutSeconds;
        private readonly int _hostRunningPollIntervalMilliseconds;
        private readonly WebJobsSdkExtensionHookProvider _bindingWebHookProvider;

        private bool _hostStarted = false;
        private IDictionary<IHttpRoute, FunctionDescriptor> _httpFunctions;
        private HttpRouteCollection _httpRoutes;
        private HttpRequestManager _httpRequestManager;

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IScriptHostFactory scriptHostFactory = null,
            ISecretsRepositoryFactory secretsRepositoryFactory = null,
            int hostTimeoutSeconds = WebScriptHostHandler.HostTimeoutSeconds,
            int hostPollingIntervalMilliseconds = WebScriptHostHandler.HostPollingIntervalMilliseconds)
            : base(config, settingsManager, scriptHostFactory, eventManager)
        {
            _config = config;
            _metricsLogger = new WebHostMetricsLogger();
            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _webHostSettings = webHostSettings;
            _hostTimeoutSeconds = hostTimeoutSeconds;
            _hostRunningPollIntervalMilliseconds = hostPollingIntervalMilliseconds;

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
            _swaggerDocumentManager = new SwaggerDocumentManager(config);

            var secretsRepository = secretsRepositoryFactory.Create(settingsManager, webHostSettings, config);
            _secretManager = secretManagerFactory.Create(settingsManager, config.TraceWriter, config.HostConfig.LoggerFactory, secretsRepository);

            _bindingWebHookProvider = new WebJobsSdkExtensionHookProvider(_secretManager);
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings,
            IScriptHostFactory scriptHostFactory)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, scriptHostFactory, new DefaultSecretsRepositoryFactory())
        {
        }

        public WebScriptHostManager(ScriptHostConfiguration config,
            ISecretManagerFactory secretManagerFactory,
            IScriptEventManager eventManager,
            ScriptSettingsManager settingsManager,
            WebHostSettings webHostSettings)
            : this(config, secretManagerFactory, eventManager, settingsManager, webHostSettings, new ScriptHostFactory())
        {
        }

        internal WebJobsSdkExtensionHookProvider BindingWebHookProvider => _bindingWebHookProvider;

        public ISecretManager SecretManager => _secretManager;

        public HostPerformanceManager PerformanceManager => _performanceManager;

        public ISwaggerDocumentManager SwaggerDocumentManager => _swaggerDocumentManager;

        public HttpRequestManager HttpRequestManager => _httpRequestManager;

        public virtual bool Initialized
        {
            get
            {
                return _hostStarted;
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

        public IReadOnlyDictionary<IHttpRoute, FunctionDescriptor> HttpFunctions
        {
            get
            {
                return _httpFunctions as IReadOnlyDictionary<IHttpRoute, FunctionDescriptor>;
            }
        }

        public async Task<HttpResponseMessage> HandleRequestAsync(FunctionDescriptor function, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // ensure that the host is ready to process requests
            await DelayUntilHostReady(_hostTimeoutSeconds, _hostRunningPollIntervalMilliseconds);

            // All authentication is assumed to have been done on the request
            // BEFORE this method is called
            var arguments = GetFunctionArguments(function, request);

            // Suspend the current synchronization context so we don't pass the ASP.NET
            // context down to the function.
            using (var syncContextSuspensionScope = new SuspendedSynchronizationContextScope())
            {
                // Add the request to the logging scope. This allows the App Insights logger to
                // record details about the request.
                ILoggerFactory loggerFactory = _config.HostConfig.GetService<ILoggerFactory>();
                ILogger logger = loggerFactory.CreateLogger(LogCategories.Function);
                var scopeState = new Dictionary<string, object>()
                {
                    [ScriptConstants.LoggerHttpRequest] = request
                };
                using (logger.BeginScope(scopeState))
                {
                    await Instance.CallAsync(function.Name, arguments, cancellationToken);
                }
            }

            // Get the response
            HttpResponseMessage response = null;
            if (!request.Properties.TryGetValue<HttpResponseMessage>(ScriptConstants.AzureFunctionsHttpResponseKey, out response))
            {
                // the function was successful but did not write an explicit response
                response = new HttpResponseMessage(HttpStatusCode.OK);
            }

            return response;
        }

        public void Initialize()
        {
            lock (_syncLock)
            {
                if (!_hostStarted)
                {
                    if (!_webHostSettings.IsSelfHost)
                    {
                        HostingEnvironment.QueueBackgroundWorkItem((ct) => RunAndBlock(ct));
                    }
                    else
                    {
                        Task.Run(() => RunAndBlock());
                    }

                    _hostStarted = true;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                (_secretManager as IDisposable)?.Dispose();
                _metricsLogger?.Dispose();
                _httpRoutes?.Dispose();
            }

            base.Dispose(disposing);
        }

        // this is for testing only
        internal static void ResetStandbyMode()
        {
            _standbyMode = null;
        }

        private static MethodInfo CreateGetWebHookDataMethodInfo()
        {
            return typeof(WebHookHandlerContextExtensions).GetMethod("GetDataOrDefault", BindingFlags.Public | BindingFlags.Static);
        }

        private static Dictionary<string, object> GetFunctionArguments(FunctionDescriptor function, HttpRequestMessage request)
        {
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>();

            if (triggerParameter.Type != typeof(HttpRequestMessage))
            {
                var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
                if (httpTrigger != null && !string.IsNullOrEmpty(httpTrigger.WebHookType))
                {
                    WebHookHandlerContext webHookContext;
                    if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookContextKey, out webHookContext))
                    {
                        // For WebHooks we want to use the WebHook library conversion methods
                        // Stuff the resolved data into the request context so the HttpTrigger binding
                        // can access it
                        var webHookData = GetWebHookData(triggerParameter.Type, webHookContext);
                        request.Properties.Add(HttpExtensionConstants.AzureWebJobsWebHookDataKey, webHookData);
                    }
                }

                // see if the function defines a parameter to receive the HttpRequestMessage and
                // if so, pass it along
                ParameterDescriptor requestParameter = function.Parameters.FirstOrDefault(p => p.Type == typeof(HttpRequestMessage));
                if (requestParameter != null)
                {
                    arguments.Add(requestParameter.Name, request);
                }
            }

            arguments.Add(triggerParameter.Name, request);

            return arguments;
        }

        private static object GetWebHookData(Type dataType, WebHookHandlerContext context)
        {
            MethodInfo getDataMethod = _getWebHookDataMethod.Value.MakeGenericMethod(dataType);
            return getDataMethod.Invoke(null, new object[] { context });
        }

        public FunctionDescriptor GetHttpFunctionOrNull(HttpRequestMessage request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (_httpFunctions == null || _httpFunctions.Count == 0)
            {
                return null;
            }

            FunctionDescriptor function = null;
            var routeData = _httpRoutes.GetRouteData(request);
            if (routeData != null)
            {
                _httpFunctions.TryGetValue(routeData.Route, out function);
                AddRouteDataToRequest(routeData, request);
            }

            return function;
        }

        internal static void AddRouteDataToRequest(IHttpRouteData routeData, HttpRequestMessage request)
        {
            if (routeData.Values != null)
            {
                Dictionary<string, object> routeDataValues = new Dictionary<string, object>();
                foreach (var pair in routeData.Values)
                {
                    // translate any unspecified optional parameters to null values
                    // unspecified values still need to be included as part of binding data
                    // for correct binding to occur
                    var value = pair.Value != RouteParameter.Optional ? pair.Value : null;
                    routeDataValues.Add(pair.Key, value);
                }

                request.Properties.Add(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, routeDataValues);
            }
        }

        protected override void OnInitializeConfig(ScriptHostConfiguration config)
        {
            base.OnInitializeConfig(config);

            // Note: this method can be called many times for the same ScriptHostConfiguration
            // so no changes should be made to the configuration itself. It is safe to modify
            // ScriptHostConfiguration.Host config though, since the inner JobHostConfiguration
            // is created anew on each restart.

            // Add our WebHost specific services
            var hostConfig = config.HostConfig;
            hostConfig.AddService<IMetricsLogger>(_metricsLogger);
            hostConfig.AddService<IWebHookProvider>(this._bindingWebHookProvider);
            hostConfig.AddService<IWebJobsExceptionHandler>(_exceptionHandler);

            // HostId may be missing in local test scenarios.
            var hostId = hostConfig.HostId ?? "default";
            Func<string, FunctionDescriptor> funcLookup = (name) => this.Instance.GetFunctionOrNull(name);
            var loggingConnectionString = config.HostConfig.DashboardConnectionString;
            var instanceLogger = new FunctionInstanceLogger(funcLookup, _metricsLogger, hostId, loggingConnectionString, config.TraceWriter);
            hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(instanceLogger);

            // disable standard Dashboard logging (enabling Table logging above)
            hostConfig.DashboardConnectionString = null;
        }

        protected override void OnHostCreated()
        {
            if (InStandbyMode)
            {
                Instance?.TraceWriter.Info("Host is in standby mode");
            }

            InitializeHttp();

            base.OnHostCreated();
        }

        protected override void OnHostStarted()
        {
            if (!InStandbyMode)
            {
                // Purge any old Function secrets
                _secretManager.PurgeOldSecretsAsync(Instance.ScriptConfig.RootScriptPath, Instance.TraceWriter, Instance.Logger);
            }

            base.OnHostStarted();
        }

        private void InitializeHttp()
        {
            // get the registered http configuration from the extension registry
            var extensions = Instance.ScriptConfig.HostConfig.GetService<IExtensionRegistry>();
            var httpConfig = extensions.GetExtensions<IExtensionConfigProvider>().OfType<HttpExtensionConfiguration>().Single();

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            InitializeHttpFunctions(Instance.Functions, httpConfig);

            // since the request manager is created based on configurable
            // settings, it has to be recreated when host config changes
            _httpRequestManager = new WebScriptHostRequestManager(httpConfig, PerformanceManager, _metricsLogger, _config.TraceWriter);
        }

        private void InitializeHttpFunctions(IEnumerable<FunctionDescriptor> functions, HttpExtensionConfiguration httpConfig)
        {
            // we must initialize the route factory here AFTER full configuration
            // has been resolved so we apply any route prefix customizations
            var functionHttpRouteFactory = new HttpRouteFactory(httpConfig.RoutePrefix);

            // Proxies do not honor the route prefix defined in host.json
            var proxyHttpRouteFactory = new HttpRouteFactory(string.Empty);

            _httpFunctions = new Dictionary<IHttpRoute, FunctionDescriptor>();
            _httpRoutes = new HttpRouteCollection();

            // Proxy routes will take precedence over http trigger functions and http trigger
            // routes so they will be added first to the list of http routes.
            var orderdFunctions = functions.OrderBy(f => f.Metadata.IsProxy ? 0 : 1);

            foreach (var function in orderdFunctions)
            {
                var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
                if (httpTrigger != null)
                {
                    IHttpRoute httpRoute = null;
                    IEnumerable<HttpMethod> httpMethods = null;
                    if (httpTrigger.Methods != null)
                    {
                        httpMethods = httpTrigger.Methods.Select(p => new HttpMethod(p)).ToArray();
                    }
                    var httpRouteFactory = function.Metadata.IsProxy ? proxyHttpRouteFactory : functionHttpRouteFactory;
                    if (httpRouteFactory.TryAddRoute(function.Metadata.Name, httpTrigger.Route, httpMethods, _httpRoutes, out httpRoute))
                    {
                        _httpFunctions.Add(httpRoute, function);
                    }
                }
            }
        }

        public override void Shutdown()
        {
            string message = "Environment shutdown has been triggered. Stopping host and signaling shutdown.";
            Instance?.TraceWriter.Info(message);
            Instance?.Logger?.LogInformation(message);

            Stop();
            HostingEnvironment.InitiateShutdown();
        }

        public async Task DelayUntilHostReady(int timeoutSeconds = WebScriptHostHandler.HostTimeoutSeconds, int pollingIntervalMilliseconds = WebScriptHostHandler.HostPollingIntervalMilliseconds, bool throwOnFailure = true)
        {
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);
            TimeSpan delay = TimeSpan.FromMilliseconds(pollingIntervalMilliseconds);
            TimeSpan timeWaited = TimeSpan.Zero;

            while (!CanInvoke() &&
                    State != ScriptHostState.Error &&
                    (timeWaited < timeout))
            {
                await Task.Delay(delay);
                timeWaited += delay;
            }

            if (throwOnFailure && !CanInvoke())
            {
                var errorResponse = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent("Function host is not running.")
                };
                throw new HttpResponseException(errorResponse);
            }
        }
    }
}