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

        public WebScriptHostManager(ScriptHostConfiguration config) : base (config)
        {
            _traceWriter = config.TraceWriter;
        }

        private IDictionary<string, HttpFunctionInfo> HttpFunctions { get; set; }

        public async Task<HttpResponseMessage> HandleRequestAsync(HttpFunctionInfo functionInfo, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // All authentication is assumed to have been done on the request
            // BEFORE this method is called

            // Invoke the function
            ParameterDescriptor triggerParameter = functionInfo.Function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { triggerParameter.Name, request }
            };
            await Instance.CallAsync(functionInfo.Function.Name, arguments, cancellationToken);

            // Get the response
            HttpResponseMessage response = null;
            if (!request.Properties.TryGetValue<HttpResponseMessage>("MS_AzureFunctionsHttpResponse", out response))
            {
                // the function was successful but did not write an explicit response
                response = new HttpResponseMessage(HttpStatusCode.OK);
            }

            return response;
        }

        public HttpFunctionInfo GetHttpFunctionOrNull(Uri uri)
        {
            HttpFunctionInfo function = null;

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            // Parse the route (e.g. "api/myfunc") to get 'myfunc"
            // including any path after "api/"
            string route = uri.AbsolutePath;
            int idx = route.ToLowerInvariant().IndexOf("api");
            if (idx > 0)
            {
                idx = route.IndexOf('/', idx);
                route = route.Substring(idx + 1).Trim('/');

                HttpFunctions.TryGetValue(route.ToLowerInvariant(), out function);
            }

            return function;
        }

        protected override void OnHostCreated()
        {
            base.OnHostCreated();

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            HttpFunctions = new Dictionary<string, HttpFunctionInfo>();
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

                    HttpFunctionInfo functionInfo = new HttpFunctionInfo
                    {
                        Function = function
                    };

                    functionInfo.WebHookReceiver = (string)httpTriggerBinding["webHookReceiver"];

                    HttpFunctions.Add(route.ToLowerInvariant(), functionInfo);
                }
            }
        }
    }
}