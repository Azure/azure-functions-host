// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.AspNet.WebHooks;
using Microsoft.AspNet.WebHooks.Config;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Description;

namespace Microsoft.Azure.WebJobs.Script.WebHost.WebHooks
{
    /// <summary>
    /// Class managing routing of requests to registered WebHook Receivers. It initializes an
    /// <see cref="HttpConfiguration"/> and loads all registered WebHook Receivers.
    /// </summary>
    public class WebHookReceiverManager : IDisposable
    {
        internal const string AzureFunctionsCallbackKey = "MS_AzureFunctionsCallback";
        
        private readonly Dictionary<string, IWebHookReceiver> _receiverLookup;
        private HttpConfiguration _httpConfiguration;
        private bool disposedValue = false;

        public WebHookReceiverManager(SecretManager secretManager)
        {
            _httpConfiguration = new HttpConfiguration();

            var builder = new ContainerBuilder();
            builder.RegisterInstance<IWebHookHandler>(new DelegatingWebHookHandler());
            builder.RegisterInstance<IWebHookReceiverConfig>(new DynamicWebHookReceiverConfig(secretManager));
            var container = builder.Build();

            WebHooksConfig.Initialize(_httpConfiguration);

            _httpConfiguration.DependencyResolver = new AutofacWebApiDependencyResolver(container);

            IEnumerable<IWebHookReceiver> receivers = _httpConfiguration.DependencyResolver.GetReceivers();
            _receiverLookup = receivers.ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        }

        public async Task<HttpResponseMessage> HandleRequestAsync(FunctionDescriptor function, HttpRequestMessage request, Func<HttpRequestMessage, Task<HttpResponseMessage>> invokeFunction)
        {
            // First check if there is a registered WebHook Receiver for this request, and if
            // so use it
            HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
            string webHookReceiver = httpFunctionMetadata.WebHookType;
            IWebHookReceiver receiver = null;
            if (string.IsNullOrEmpty(webHookReceiver) || !_receiverLookup.TryGetValue(webHookReceiver, out receiver))
            {
                // The function is not correctly configured. Log an error and return 500
                string configurationError = string.Format(CultureInfo.InvariantCulture, "Invalid WebHook configuration. Unable to find a receiver for WebHook type '{0}'", webHookReceiver);
                function.Invoker.OnError(new FunctionInvocationException(configurationError));

                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }

            HttpRequestContext context = new HttpRequestContext
            {
                Configuration = _httpConfiguration
            };
            request.SetConfiguration(_httpConfiguration);

            // add the anonymous handler function from above to the request properties
            // so our custom WebHookHandler can invoke it at the right time
            request.Properties.Add(AzureFunctionsCallbackKey, invokeFunction);

            // Request content can't be read multiple
            // times, so this forces it to buffer
            await request.Content.LoadIntoBufferAsync();

            string receiverId = function.Name.ToLowerInvariant();
            return await receiver.ReceiveAsync(receiverId, context, request);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    if (_httpConfiguration != null)
                    {
                        _httpConfiguration.Dispose();
                    }
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Custom <see cref="WebHookHandler"/> used to integrate ASP.NET WebHooks in our request pipeline.
        /// When a request is dispatched to a <see cref="WebHookReceiver"/>, after validating the request
        /// fully, it will delegate to this handler, allowing us to resume processing and dispatch the request
        /// to the function.
        /// </summary>
        private class DelegatingWebHookHandler : WebHookHandler
        {
            public override async Task ExecuteAsync(string receiver, WebHookHandlerContext context)
            {
                // At this point, the WebHookReceiver has validated this request, so we
                // now need to dispatch it to the actual function.

                // get the callback from request properties
                var requestHandler = (Func<HttpRequestMessage, Task<HttpResponseMessage>>)
                    context.Request.Properties[AzureFunctionsCallbackKey];

                context.Request.Properties.Add(ScriptConstants.AzureFunctionsWebHookContextKey, context);

                // Invoke the function
                context.Response = await requestHandler(context.Request);
            }
        }
    }
}