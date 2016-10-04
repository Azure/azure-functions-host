// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
using Microsoft.Azure.WebJobs.Script.Binding.Http;
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
        private readonly SecretManager _secretManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly IWebJobsExceptionHandler _exceptionHandler;
        private readonly ScriptHostConfiguration _config;
        private readonly object _syncLock = new object();
        private HttpRouteFactory _httpRouteFactory;
        private bool _warmupComplete = false;
        private bool _hostStarted = false;

        public WebScriptHostManager(ScriptHostConfiguration config, SecretManager secretManager, WebHostSettings webHostSettings) : base(config)
        {
            _config = config;
            _metricsLogger = new WebHostMetricsLogger();
            _exceptionHandler = new WebScriptHostExceptionHandler(this);
            _secretManager = secretManager;
            _webHostSettings = webHostSettings;
        }

        public static bool IsAzureEnvironment
        {
            get
            {
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsiteInstanceId));
            }
        }

        private IDictionary<IHttpRoute, FunctionDescriptor> HttpFunctions { get; set; }

        private HttpRouteCollection HttpRoutes { get; set; }

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
                if (Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebsitePlaceholderMode) == "1")
                {
                    return true;
                }

                // no longer standby mode
                _standbyMode = false;

                return _standbyMode.Value;
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

                host = ScriptHost.Create(config);
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
                if (_secretManager != null)
                {
                    _secretManager.Dispose();
                }

                if (_metricsLogger != null)
                {
                    _metricsLogger.Dispose();
                }

                if (HttpRoutes != null)
                {
                    HttpRoutes.Dispose();
                }
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

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            FunctionDescriptor function = null;
            var routeData = HttpRoutes.GetRouteData(request);
            if (routeData != null)
            {
                HttpFunctions.TryGetValue(routeData.Route, out function);

                Dictionary<string, object> routeDataValues = null;
                if (routeData.Values != null)
                {
                    routeDataValues = new Dictionary<string, object>();
                    foreach (var pair in routeData.Values)
                    {
                        // filter out any unspecified optional parameters
                        if (pair.Value != RouteParameter.Optional)
                        {
                            routeDataValues.Add(pair.Key, pair.Value);
                        }
                    }
                }
                
                request.Properties.Add(ScriptConstants.AzureFunctionsHttpRouteDataKey, routeDataValues);
            }

            return function;
        }

        protected override void OnInitializeConfig(ScriptHostConfiguration config)
        {
            base.OnInitializeConfig(config);

            // Add our WebHost specific services
            var hostConfig = config.HostConfig;
            hostConfig.AddService<IMetricsLogger>(_metricsLogger);

            var systemEventGenerator = hostConfig.GetService<IEventGenerator>() ?? new EventGenerator();
            var systemTraceWriter = new SystemTraceWriter(systemEventGenerator, TraceLevel.Verbose);
            if (config.TraceWriter != null)
            {
                config.TraceWriter = new CompositeTraceWriter(new TraceWriter[] { config.TraceWriter, systemTraceWriter });
            }
            else
            {
                config.TraceWriter = systemTraceWriter;
            }

            // Add our exception handler
            hostConfig.AddService<IWebJobsExceptionHandler>(_exceptionHandler);

            // Register the new "FastLogger" for Dashboard support
            var dashboardString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Dashboard);
            if (dashboardString != null)
            {
                var fastLogger = new FastLogger(dashboardString);
                hostConfig.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(fastLogger);
            }
            hostConfig.DashboardConnectionString = null; // disable slow logging
        }

        protected override void OnHostStarted()
        {
            base.OnHostStarted();

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            InitializeHttpFunctions(Instance.Functions);

            // Purge any old Function secrets
            _secretManager.PurgeOldFiles(Instance.ScriptConfig.RootScriptPath, Instance.TraceWriter);
        }

        internal void InitializeHttpFunctions(Collection<FunctionDescriptor> functions)
        {
            // we must initialize the route factory here AFTER full configuration
            // has been resolved so we apply any route prefix customizations
            _httpRouteFactory = new HttpRouteFactory(_config.HttpRoutePrefix);

            HttpFunctions = new Dictionary<IHttpRoute, FunctionDescriptor>();
            HttpRoutes = new HttpRouteCollection();

            foreach (var function in functions)
            {
                HttpTriggerBindingMetadata httpTriggerBinding = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.SingleOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
                if (httpTriggerBinding != null)
                {
                    string functionName = function.Metadata.Name;
                    string route = httpTriggerBinding.Route;
                    if (string.IsNullOrEmpty(route))
                    {
                        // if no explicit route is provided, default to the function name
                        route = functionName;
                    }

                    var httpRoute = _httpRouteFactory.AddRoute(functionName, route, HttpRoutes);
                    HttpFunctions.Add(httpRoute, function);
                }
            }
        }
    }
}