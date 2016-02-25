// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;

namespace WebJobs.Script.WebHost
{
    public class WebScriptHostManager : ScriptHostManager
    {
        public WebScriptHostManager(ScriptHostConfiguration config) : base (config)
        {
        }

        private IDictionary<string, FunctionDescriptor> HttpFunctions { get; set; }

        public async Task<HttpResponseMessage> HandleRequestAsync(FunctionDescriptor function, HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // All authentication is assumed to have been done on the request
            // BEFORE this method is called

            // Invoke the function
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>
            {
                { triggerParameter.Name, request }
            };
            await Instance.CallAsync(function.Name, arguments, cancellationToken);

            // Get the response
            HttpResponseMessage response = null;
            if (!request.Properties.TryGetValue<HttpResponseMessage>("MS_AzureFunctionsHttpResponse", out response))
            {
                // the function was successful but did not write an explicit response
                response = new HttpResponseMessage(HttpStatusCode.OK);
            }

            return response;
        }

        public FunctionDescriptor GetHttpFunctionOrNull(Uri uri)
        {
            if (uri == null)
            {
                throw new ArgumentNullException("uri");
            }

            FunctionDescriptor function = null;

            if (HttpFunctions == null || HttpFunctions.Count == 0)
            {
                return null;
            }

            // Parse the route (e.g. "api/myfunc") to get 'myfunc"
            // including any path after "api/"
            string route = uri.AbsolutePath;
            int idx = route.ToLowerInvariant().IndexOf("api", StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                idx = route.IndexOf('/', idx);
                route = route.Substring(idx + 1).Trim('/');

                HttpFunctions.TryGetValue(route.ToLowerInvariant(), out function);
            }

            return function;
        }

        protected override void OnHostStarted()
        {
            base.OnHostStarted();

            // whenever the host is created (or recreated) we build a cache map of
            // all http function routes
            HttpFunctions = new Dictionary<string, FunctionDescriptor>();
            foreach (var function in Instance.Functions)
            {
                HttpBindingMetadata httpTriggerBinding = (HttpBindingMetadata)function.Metadata.InputBindings.SingleOrDefault(p => p.Type == BindingType.HttpTrigger);
                if (httpTriggerBinding != null)
                {
                    string route = httpTriggerBinding.Route;
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