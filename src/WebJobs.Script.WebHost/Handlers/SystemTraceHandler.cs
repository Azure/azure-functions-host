// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
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
            var details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() }
            };
            TraceWriter.Info($"Executing HTTP request: {details}");

            var response = await base.SendAsync(request, cancellationToken);

            details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() },
                { "authorizationLevel", request.GetAuthorizationLevel().ToString() }
            };
            TraceWriter.Info($"Executed HTTP request: {details}");

            details = new JObject
            {
                { "requestId", request.GetRequestId() },
                { "status", response.StatusCode.ToString() }
            };
            TraceWriter.Info($"Response details: {details}");

            return response;
        }
    }
}