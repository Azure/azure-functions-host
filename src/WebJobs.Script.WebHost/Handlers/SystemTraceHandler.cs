// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

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

        public SystemTraceHandler(HttpConfiguration config)
        {
            _config = config;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var traceWriter = _config.DependencyResolver.GetService<TraceWriter>();
            var details = new JObject
            {
                { "id", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() }
            };
            traceWriter.Info($"Executing HTTP request: {details}");

            var response = await base.SendAsync(request, cancellationToken);

            details = new JObject
            {
                { "id", request.GetRequestId() },
                { "method", request.Method.ToString() },
                { "uri", request.RequestUri.LocalPath.ToString() },
                { "authorizationLevel", request.GetAuthorizationLevel().ToString() }
            };
            traceWriter.Info($"Executed HTTP request: {details}");

            details = new JObject
            {
                { "id", request.GetRequestId() },
                { "status", response.StatusCode.ToString() }
            };
            traceWriter.Info($"Response details: {details}");

            return response;
        }
    }
}