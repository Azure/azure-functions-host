// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Host.Timers;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

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
        private bool _warmupComplete = false;
        private bool _hostStarted = false;
        private IDictionary<IHttpRoute, FunctionDescriptor> _httpFunctions;
        private HttpRouteCollection _httpRoutes;

        public WebScriptHostManager(ScriptHostConfiguration config, ISecretManagerFactory secretManagerFactory, ScriptSettingsManager settingsManager, WebHostSettings webHostSettings, IScriptHostFactory scriptHostFactory = null)
            : base(config, settingsManager, scriptHostFactory)
        {
            _config = config;
            _metricsLogger = new WebHostMetricsLogger();
            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _webHostSettings = webHostSettings;

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

            _secretManager = secretManagerFactory.Create(settingsManager, config.TraceWriter, new FileSystemSecretsRepository(webHostSettings.SecretsPath));
            _performanceManager = new HostPerformanceManager(settingsManager);
            _swaggerDocumentManager = new SwaggerDocumentManager(config);
        }

        public WebScriptHostManager(ScriptHostConfiguration config, ISecretManagerFactory secretManagerFactory, ScriptSettingsManager settingsManager, WebHostSettings webHostSettings)
            : this(config, secretManagerFactory, settingsManager, webHostSettings, new ScriptHostFactory())
        {
        }

        public ISecretManager SecretManager => _secretManager;

        public HostPerformanceManager PerformanceManager => _performanceManager;

        public ISwaggerDocumentManager SwaggerDocumentManager => _swaggerDocumentManager;

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

        public IReadOnlyDictionary<IHttpRoute, FunctionDescriptor> HttpFunctions
        {
            get
            {
                return _httpFunctions as IReadOnlyDictionary<IHttpRoute, FunctionDescriptor>;
            }
        }

        public async Task<HttpResponseMessage> HandleRequestAsync(FunctionDescriptor function, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // All authentication is assumed to have been done on the request
            // BEFORE this method is called

            Dictionary<string, object> arguments = GetFunctionArguments(function, request);

            // Suspend the current synchronization context so we don't pass the ASP.NET
            // context down to the function.
            using (var syncContextSuspensionScope = new SuspendedSynchronizationContextScope())
            {
                await Instance.CallAsync(function.Name, arguments, cancellationToken);
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
                if (InStandbyMode)
                {
                    if (!_warmupComplete)
                    {
                        if (!_webHostSettings.IsSelfHost)
                        {
                            HostingEnvironment.QueueBackgroundWorkItem((ct) => WarmUp(_webHostSettings));
                        }
                        else
                        {
                            Task.Run(() => WarmUp(_webHostSettings));
                        }

                        _warmupComplete = true;
                    }
                }
                else if (!_hostStarted)
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

        public static void WarmUp(WebHostSettings settings)
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

                // read in the F# function
                functionPath = Path.Combine(rootPath, "Test-FSharp");
                Directory.CreateDirectory(functionPath);
                content = ReadResourceString("Functions.Test_FSharp.function.json");
                File.WriteAllText(Path.Combine(functionPath, "function.json"), content);
                content = ReadResourceString("Functions.Test_FSharp.run.fsx");
                File.WriteAllText(Path.Combine(functionPath, "run.fsx"), content);

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

                host = ScriptHost.Create(new NullScriptHostEnvironment(), config, ScriptSettingsManager.Instance);
                traceWriter.Info(string.Format("Starting Host (Id={0})", host.ScriptConfig.HostConfig.HostId));

                host.Start();

                var arguments = new Dictionary<string, object>
                {
                    { "input", "{}" }
                };
                host.CallAsync("Test-CSharp", arguments).Wait();
                host.CallAsync("Test-FSharp", arguments).Wait();
                host.Stop();

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
                HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
                if (!string.IsNullOrEmpty(httpFunctionMetadata.WebHookType))
                {
                    WebHookHandlerContext webHookContext;
                    if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookContextKey, out webHookContext))
                    {
                        // For WebHooks we want to use the WebHook library conversion methods
                        // Stuff the resolved data into the request context so the HttpTrigger binding
                        // can access it
                        var webHookData = GetWebHookData(triggerParameter.Type, webHookContext);
                        request.Properties.Add(ScriptConstants.AzureFunctionsWebHookDataKey, webHookData);
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

        private static string ReadResourceString(string fileName)
        {
            string resourcePath = string.Format("Microsoft.Azure.WebJobs.Script.WebHost.Resources.{0}", fileName);
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (StreamReader reader = new StreamReader(assembly.GetManifestResourceStream(resourcePath)))
            {
                return reader.ReadToEnd();
            }
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

                request.Properties.Add(ScriptConstants.AzureFunctionsHttpRouteDataKey, routeDataValues);
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

            // Add our exception handler
            hostConfig.AddService<IWebJobsExceptionHandler>(_exceptionHandler);

            // Register the new "FastLogger" for Dashboard support
            var dashboardString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Dashboard);
            if (dashboardString != null)
            {
                // hostId may be missing in local test scenarios. 
                var hostId = config.HostConfig.HostId ?? "default";
                var fastLogger = new FastLogger(hostId, dashboardString, config.TraceWriter);
                hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(fastLogger);
            }
            hostConfig.DashboardConnectionString = null; // disable slow logging
        }

        protected override void OnHostCreated()
        {
            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            InitializeHttpFunctions(Instance.Functions);

            base.OnHostCreated();
        }

        protected override void OnHostStarted()
        {
            // Purge any old Function secrets
            _secretManager.PurgeOldSecretsAsync(Instance.ScriptConfig.RootScriptPath, Instance.TraceWriter);

            base.OnHostStarted();
        }

        internal void InitializeHttpFunctions(IEnumerable<FunctionDescriptor> functions)
        {
            // we must initialize the route factory here AFTER full configuration
            // has been resolved so we apply any route prefix customizations
            HttpRouteFactory httpRouteFactory = new HttpRouteFactory(_config.HttpRoutePrefix);

            _httpFunctions = new Dictionary<IHttpRoute, FunctionDescriptor>();
            _httpRoutes = new HttpRouteCollection();

            foreach (var function in functions)
            {
                var httpTriggerBinding = function.Metadata.InputBindings.OfType<HttpTriggerBindingMetadata>().SingleOrDefault();
                if (httpTriggerBinding != null)
                {
                    IHttpRoute httpRoute = null;
                    if (httpRouteFactory.TryAddRoute(function.Metadata.Name, httpTriggerBinding.Route, httpTriggerBinding.Methods, _httpRoutes, out httpRoute))
                    {
                        _httpFunctions.Add(httpRoute, function);
                    }
                }
            }
        }

        public override void Shutdown()
        {
            Instance?.TraceWriter.Info("Environment shutdown has been triggered. Stopping host and signaling shutdown.");

            Stop();
            HostingEnvironment.InitiateShutdown();
        }
    }
}