// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Microsoft.AspNet.Routing.Template;
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
        private readonly IMetricsLogger _metricsLogger;
        private readonly SecretManager _secretManager;

        public WebScriptHostManager(ScriptHostConfiguration config, SecretManager secretManager) : base(config)
        {
            _metricsLogger = new WebHostMetricsLogger();
            _secretManager = secretManager;
        }

        private IDictionary<string, FunctionDescriptor> HttpFunctions { get; set; }

        private List<KeyValuePair<string, FunctionDescriptor>> RouteTemplates { get; set; }

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

            //extract all possible parameter values
            var route = RoutingUtility.ExtractRouteTemplateFromMetadata(function.Metadata);
            var parameters = RoutingUtility.ExtractRouteParameters(route, request);
            
            foreach (var pair in parameters)
            {
                arguments.Add(pair.Key, pair.Value);
            }

            var queryStringParameters = request.GetQueryNameValuePairs().ToImmutableDictionary();
            foreach (var parameter in function.Parameters)
            {
                // if the parameter is not a binding then assume it is a 
                if (!bindingNames.Contains(parameter.Name))
                {
                    string value = null;
                    queryStringParameters.TryGetValue(parameter.Name, out value);
                    if (value != null)
                    {
                        //convert to the proper type if possible
                        object properValue = Convert.ChangeType(value, parameter.Type);
                        arguments.Add(parameter.Name, properValue);
                    }
                }
            }

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
            RoutingUtility.ClearTemplates();
            foreach (var function in functions)
            {
                HttpTriggerBindingMetadata httpTriggerBinding = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.SingleOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
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