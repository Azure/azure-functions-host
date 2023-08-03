// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Workers.Rpc;
using Yarp.ReverseProxy.Forwarder;

namespace Microsoft.Azure.WebJobs.Script.Grpc
{
    internal class DefaultHttpProxyService : IHttpProxyService, IDisposable
    {
        private readonly SocketsHttpHandler _handler;
        private readonly IHttpForwarder _httpForwarder;
        private readonly HttpMessageInvoker _messageInvoker;
        private readonly ForwarderRequestConfig _forwarderRequestConfig;

        public DefaultHttpProxyService(IHttpForwarder httpForwarder)
        {
            _httpForwarder = httpForwarder ?? throw new ArgumentNullException(nameof(httpForwarder));

            _handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false
            };

            _messageInvoker = new HttpMessageInvoker(_handler);
            _forwarderRequestConfig = new ForwarderRequestConfig
            {
                ActivityTimeout = TimeSpan.FromSeconds(240)
            };
        }

        public void Dispose()
        {
            _handler?.Dispose();
            _messageInvoker?.Dispose();
        }

        public ValueTask<ForwarderError> ForwardAsync(ScriptInvocationContext context, Uri httpUri)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Inputs is null || context.Inputs?.Count() == 0)
            {
                throw new InvalidOperationException($"The function {context.FunctionMetadata.Name} can not be evaluated since it has no inputs.");
            }

            HttpRequest httpRequest = context.Inputs.FirstOrDefault(i => i.Val is HttpRequest).Val as HttpRequest;

            if (httpRequest is null)
            {
                throw new InvalidOperationException($"Cannot proxy the HttpTrigger function {context.FunctionMetadata.Name} without an input of type {nameof(HttpRequest)}.");
            }

            HttpContext httpContext = httpRequest.HttpContext;

            httpContext.Items.Add(ScriptConstants.HttpProxyingEnabled, bool.TrueString);

            // add invocation id as correlation id
            httpRequest.Headers.TryAdd(ScriptConstants.HttpProxyCorrelationHeader, context.ExecutionContext.InvocationId.ToString());

            var aspNetTask = _httpForwarder.SendAsync(httpContext, httpUri.ToString(), _messageInvoker, _forwarderRequestConfig);

            return aspNetTask;
        }
    }
}
