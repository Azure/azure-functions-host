using System;
using System.Collections.Generic;
using System.Linq;
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

        private IDictionary<string, string> HttpFunctions { get; set; }

        public string GetMappedHttpFunction(Uri uri)
        {
            string route = uri.AbsolutePath;
            int idx = route.ToLowerInvariant().IndexOf("functions");
            idx = route.IndexOf('/', idx);
            route = route.Substring(idx + 1).Trim('/');

            string functionName;
            HttpFunctions.TryGetValue(route, out functionName);

            return functionName;
        }

        protected override void OnHostCreated()
        {
            base.OnHostCreated();

            if (_traceWriter != null)
            {
                Instance.ScriptConfig.HostConfig.Tracing.Tracers.Add(_traceWriter);
            }

            // whenever the host is created (or recreated) we build a map of
            // all http function routes
            Dictionary<string, string> httpFunctionMap = new Dictionary<string, string>();
            foreach (var function in Instance.Functions)
            {
                JObject functionConfig = function.Metadata.Configuration;
                JObject bindings = (JObject)functionConfig["bindings"];
                JArray inputs = (JArray)bindings["input"];
                JObject httpBinding = (JObject)inputs.FirstOrDefault(p => (string)p["type"] == "httpTrigger");
                if (httpBinding != null)
                {
                    string route = (string)httpBinding["route"];
                    if (!string.IsNullOrEmpty(route))
                    {
                        route += "/";
                    }
                    route += function.Name;
                    httpFunctionMap.Add(route.ToLowerInvariant(), function.Name);
                }
            }

            HttpFunctions = httpFunctionMap;
        }
    }
}