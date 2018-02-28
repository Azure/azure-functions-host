// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.WebJobs.Host;
using Newtonsoft.Json.Linq;
using Microsoft.Azure.WebJobs.Script;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Handlers
{
    public class SystemTraceHandler : DelegatingHandler
    {
        private readonly HttpConfiguration _config;
        private TraceWriter _traceWriter;

        public SystemTraceHandler(HttpConfiguration config)
        {
            _config = config;
        }

        private TraceWriter TraceWriter
        {
            get
            {
                if (_traceWriter == null)
                {
                    _traceWriter = _config.DependencyResolver
                        .GetService<TraceWriter>()
                        .WithDefaults(ScriptConstants.TraceSourceHttpHandler);
                }

                return _traceWriter;
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.IsColdStart())
            {
                // for cold start requests we want to measure the request
                // pipeline dispatch time
                // important that this stopwatch is started as early as possible
                // in the pipeline (in this case, in our first handler)
                var sw = new Stopwatch();
                sw.Start();
                request.Properties.Add(ScriptConstants.AzureFunctionsColdStartKey, sw);
            }

            Dictionary<string, object> traceProperties = new Dictionary<string, object>();

            var details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() }
            };
            traceProperties[ScriptConstants.TracePropertyActivityIdKey] = details["requestId"];
            TraceWriter.Info($"Executing HTTP request: {details}", traceProperties);

            var response = await base.SendAsync(request, cancellationToken);

            details["authorizationLevel"] = request.GetAuthorizationLevel().ToString();
            details["status"] = response.StatusCode.ToString();
            TraceWriter.Info($"Executed HTTP request: {details}", traceProperties);

            return response;
        }
    }
}