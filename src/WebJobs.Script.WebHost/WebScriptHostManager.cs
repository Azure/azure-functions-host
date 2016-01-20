using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script;
using Newtonsoft.Json.Linq;

namespace WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        private TraceWriter _traceWriter;

        public WebScriptHostManager(ScriptHostConfiguration config, TraceWriter traceWriter) : base (config)
        {
            _traceWriter = traceWriter;
        }

        private IDictionary<string, FunctionDescriptor> HttpFunctions { get; set; }

        public async Task<HttpResponseMessage> HandleRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // All authentication is assumed to have been done on the request
            // BEFORE this method is called

            // First see if the URI maps to a function
            FunctionDescriptor function = GetHttpFunctionOrNull(request.RequestUri);
            if (function == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Invoke the function
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { triggerParameter.Name, request }
            };
            await Instance.CallAsync(function.Name, arguments, cancellationToken);

            // Get the response
            HttpResponseMessage response = (HttpResponseMessage)request.Properties["AzureWebJobs_HttpResponse"];

            return response;
        }

        private FunctionDescriptor GetHttpFunctionOrNull(Uri uri)
        {
            FunctionDescriptor function = null;

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            // Parse the route (e.g. "functions/myfunc") to get 'myfunc"
            // including any path after "functions/"
            string route = uri.AbsolutePath;
            int idx = route.ToLowerInvariant().IndexOf("functions");
            if (idx > 0)
            {
                idx = route.IndexOf('/', idx);
                route = route.Substring(idx + 1).Trim('/');

                HttpFunctions.TryGetValue(route, out function);
            }

            return function;
        }

        protected override void OnHostCreated()
        {
            base.OnHostCreated();

            if (_traceWriter != null)
            {
                Instance.ScriptConfig.HostConfig.Tracing.Tracers.Add(_traceWriter);
            }

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            HttpFunctions = new Dictionary<string, FunctionDescriptor>();
            foreach (var function in Instance.Functions)
            {
                JObject functionConfig = function.Metadata.Configuration;
                JObject bindings = (JObject)functionConfig["bindings"];
                if (bindings == null)
                {
                    return;
                }

                JArray inputs = (JArray)bindings["input"];
                if (inputs == null)
                {
                    return;
                }

                JObject httpTriggerBinding = (JObject)inputs.FirstOrDefault(p => (string)p["type"] == "httpTrigger");
                if (httpTriggerBinding != null)
                {
                    string route = (string)httpTriggerBinding["route"];
                    if (!string.IsNullOrEmpty(route))
                    {
                        route += "/";
                    }
                    route += function.Name;
                    HttpFunctions.Add(route.ToLowerInvariant(), function);
                }
            }
        }
    }
}