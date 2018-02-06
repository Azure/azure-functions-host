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
                        .WithSource(ScriptConstants.TraceSourceHttpHandler);
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

            var details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() }
            };
            WriteTraceEvent($"Executing HTTP request: {details}", request);

            var response = await base.SendAsync(request, cancellationToken);

            details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() },
                { "authorizationLevel", request.GetAuthorizationLevel().ToString() }
            };
            WriteTraceEvent($"Executed HTTP request: {details}", request);

            details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "status", response.StatusCode.ToString() }
            };
            WriteTraceEvent($"Response details: {details}", request);

            return response;
        }
        private void WriteTraceEvent(string msg, HttpRequestMessage request)
        {
            TraceEvent traceEvent = new TraceEvent(TraceLevel.Info, msg);
            object value = null;
            if (request.Properties.TryGetValue(ScriptConstants.TracePropertyScriptHostInstanceIdKey, out value) && value != null)
            {
                value.ToString();
            }
            traceEvent.Properties.Add(ScriptConstants.TracePropertyScriptHostInstanceIdKey, value);
            TraceWriter.Trace(traceEvent);
        }
    }
}