// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Hosting;
using Microsoft.AspNet.WebHooks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Host.Loggers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics;

namespace Microsoft.Azure.WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private static Lazy<MethodInfo> _getWebHookDataMethod = new Lazy<MethodInfo>(CreateGetWebHookDataMethodInfo);
        private readonly HttpMethod _defaultMethod = HttpMethod.Post;
        private static bool? _standbyMode;
        private readonly IMetricsLogger _metricsLogger;
        private readonly SecretManager _secretManager;
        private readonly WebHostSettings _webHostSettings;
        private readonly object _syncLock = new object();
        private bool _warmupComplete = false;
        private bool _hostStarted = false;

        public WebScriptHostManager(ScriptHostConfiguration config, SecretManager secretManager, WebHostSettings webHostSettings) : base(config)
        {
            _metricsLogger = new WebHostMetricsLogger();
            _secretManager = secretManager;
            _webHostSettings = webHostSettings;
        }

        public static bool IsAzureEnvironment
        {
            get
            {
                return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));
            }
        }

        private IDictionary<string, FunctionDescriptor> HttpFunctions { get; set; }

        public bool Initialized
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
                if (Environment.GetEnvironmentVariable("WEBSITE_PLACEHOLDER_MODE") == "1")
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

            Dictionary<string, object> arguments = await GetFunctionArgumentsAsync(function, request);

            // Suspend the current synchronization context so we don't pass the ASP.NET
            // context down to the function.
            using (var syncContextSuspensionScope = new SuspendedSynchronizationContextScope())
            {
                await Instance.CallAsync(function.Name, arguments, cancellationToken);
            }

            // Get the response
            HttpResponseMessage response = null;
            if (!request.Properties.TryGetValue<HttpResponseMessage>("MS_AzureFunctionsHttpResponse", out response))
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

                string content = ReadResourceString("Test.host.json");
                File.WriteAllText(Path.Combine(rootPath, "host.json"), content);

                string functionPath = Path.Combine(rootPath, "Test");
                Directory.CreateDirectory(functionPath);
                content = ReadResourceString("Test.function.json");
                File.WriteAllText(Path.Combine(functionPath, "function.json"), content);

                content = ReadResourceString("Test.run.csx");
                File.WriteAllText(Path.Combine(functionPath, "run.csx"), content);

                traceWriter.Info("Warm up functions deployed");

                ScriptHostConfiguration config = new ScriptHostConfiguration
                {
                    RootScriptPath = rootPath,
                    FileLoggingEnabled = false,
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
                host.CallAsync("Test", arguments).Wait();
                host.Stop();

                traceWriter.Info("Warm up succeeded");
            }
            catch (Exception ex)
            {
                traceWriter.Error(string.Format("Warm up failed: {0}", ex));
            }
            finally
            {
                if (host != null)
                {
                    // dispose this last, since it will dispose TraceWriter
                    host.Dispose();
                }

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

        private static async Task<Dictionary<string, object>> GetFunctionArgumentsAsync(FunctionDescriptor function, HttpRequestMessage request)
        {
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>();
            object triggerArgument = null;
            if (triggerParameter.Type == typeof(HttpRequestMessage))
            {
                triggerArgument = request;
            }
            else
            {
                // We'll replace the trigger argument but still want to flow the request
                // so add it to the arguments, as a system argument
                arguments.Add(ScriptConstants.DefaultSystemTriggerParameterName, request);

                HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
                if (!string.IsNullOrEmpty(httpFunctionMetadata.WebHookType))
                {
                    WebHookHandlerContext webHookContext;
                    if (request.Properties.TryGetValue(ScriptConstants.AzureFunctionsWebHookContextKey, out webHookContext))
                    {
                        triggerArgument = GetWebHookData(triggerParameter.Type, webHookContext);
                    }
                }

                if (triggerArgument == null)
                {
                    triggerArgument = await request.Content.ReadAsAsync(triggerParameter.Type);
                }
            }

            arguments.Add(triggerParameter.Name, triggerArgument);

            //add query string parameters that aren't satisfied by any bindings to avoid having WebJobs SDK throw an 
            //error for any parameters it doesn't find values from through bindings.
            var bindingNames = function.Metadata.Bindings.Select(b => b.Name).ToImmutableSortedSet();

            //extract all possible parameter values from the route
            var route = RoutingUtility.ExtractRouteTemplateFromMetadata(function.Metadata);
            var parameters = RoutingUtility.ExtractRouteParameters(route, request);          
            foreach (var pair in parameters)
            {
                arguments.Add(pair.Key, pair.Value);
            }

            //Extract parameters from the query string if match a parameter name that is not a binding
            var queryStringParameters = request.GetQueryNameValuePairs().ToImmutableDictionary();
            foreach (var parameter in function.Parameters)
            {
                // if the parameter is not a binding or already found in the routes then assume it is a parameter to be extracted from the query string
                if (!bindingNames.Contains(parameter.Name) && !arguments.ContainsKey(parameter.Name))
                {
                    //extract the value from the query string parameters and convert it before adding it as an argument.
                    string value = null;
                    queryStringParameters.TryGetValue(parameter.Name, out value);
                    object properValue = value != null ? Convert.ChangeType(value, parameter.Type) : null;
                    if (properValue != null)
                    {
                        arguments.Add(parameter.Name, properValue);
                    }
                }
            }

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
                throw new ArgumentNullException("request");
            }

            var uri = request.RequestUri;
            FunctionDescriptor function = null;

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            // Parse the route (e.g. "api/myfunc") to get 'myfunc"
            // including any path after "api/"
            string route = HttpUtility.UrlDecode(uri.AbsolutePath);
            int idx = route.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                idx = route.IndexOf('/', idx);
                route = route.Substring(idx + 1).Trim('/');

                route = (request.Method.Method + "/" + route).ToLowerInvariant();

                //attempt to find if any of the keys exactly match this route
                HttpFunctions.TryGetValue(route, out function);

                //if still haven't found a function look at all of the templates
                if (function == null)
                {
                    function = (from func in HttpFunctions
                                where RoutingUtility.MatchesTemplate(func.Key, route)
                                select func.Value)
                                .FirstOrDefault();
                }   
            }
            return function;
        }

        protected override void OnInitializeConfig(JobHostConfiguration config)
        {
            base.OnInitializeConfig(config);
            
            // Add our WebHost specific services
            config.AddService<IMetricsLogger>(_metricsLogger);

            // Register the new "FastLogger" for Dashboard support
            var dashboardString = AmbientConnectionStringProvider.Instance.GetConnectionString(ConnectionStringNames.Dashboard);
            if (dashboardString != null)
            {
                var fastLogger = new FastLogger(dashboardString);
                config.AddService<IAsyncCollector<FunctionInstanceLogEntry>>(fastLogger);
            }
            config.DashboardConnectionString = null; // disable slow logging 
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
            HttpFunctions = new Dictionary<string, FunctionDescriptor>();
            RoutingUtility.ClearCache();
            foreach (var function in functions)
            {
                HttpTriggerBindingMetadata httpTriggerBinding = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.SingleOrDefault(p => p.Type.Equals("httptrigger", StringComparison.OrdinalIgnoreCase));
                if (httpTriggerBinding != null)
                {
                    string route = httpTriggerBinding.Route ?? function.Name;
                    var methods = httpTriggerBinding.Methods ?? new Collection<HttpMethod>(new HttpMethod[] { _defaultMethod });
                    foreach (var method in methods)
                    {
                        HttpFunctions.Add((method + "/" + route).ToLowerInvariant(), function);
                    }
                }
            }
        }
    }
}