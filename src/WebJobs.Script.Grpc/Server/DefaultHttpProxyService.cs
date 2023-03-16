// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class DefaultHttpProxyService : IHttpProxyService
    {
        private IHttpForwarder _httpForwarder;
        private HttpMessageInvoker _messageInvoker;
        private ForwarderRequestConfig _forwarderRequestConfig;
        private string _proxyEndpoint;

        public DefaultHttpProxyService(IHttpForwarder httpForwarder)
        {
            _httpForwarder = httpForwarder;

            var handler = new SocketsHttpHandler();
            _messageInvoker = new HttpMessageInvoker(handler);
            _forwarderRequestConfig = new ForwarderRequestConfig();

            // TODO: Update this logic. Port should come through configuration.
            var port = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_HTTP_PROXY_PORT") ?? "5555";

            _proxyEndpoint = "http://localhost:" + port;
        }

        public ValueTask<ForwarderError> Forward(ScriptInvocationContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (context.Inputs is null)
            {
                throw new ArgumentNullException(nameof(context.Inputs));
            }

            HttpRequest httpRequest = context.Inputs.FirstOrDefault(i => i.Val is HttpRequest).Val as HttpRequest;

            if (httpRequest is null)
            {
                throw new InvalidOperationException($"Cannot proxy an HttpTrigger Function without an input of type {nameof(HttpRequest)}.");
            }

            HttpContext httpContext = httpRequest.HttpContext;

            httpContext.Items.Add("IsHttpProxying", bool.TrueString);

            // add invocation id as correlation id
            // TODO: add "invocation-id" as a constant somewhere / maybe find a better name
            httpRequest.Headers.TryAdd("invocation-id", context.ExecutionContext.InvocationId.ToString());

            var aspNetTask = _httpForwarder.SendAsync(httpContext, _proxyEndpoint, _messageInvoker, _forwarderRequestConfig);

            return aspNetTask;
        }
    }
}
