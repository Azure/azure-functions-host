﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Controllers;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.AspNet.WebHooks;
using Microsoft.AspNet.WebHooks.Config;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Azure.WebJobs.Script.Binding;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;

namespace Microsoft.Azure.WebJobs.Script.WebHost.WebHooks
{
    /// <summary>
    /// Class managing routing of requests to registered WebHook Receivers. It initializes an
    /// <see cref="HttpConfiguration"/> and loads all registered WebHook Receivers.
    /// </summary>
    public class WebHookReceiverManager : IDisposable
    {
        public const string FunctionsClientIdHeaderName = "x-functions-clientid";
        public const string FunctionsClientIdQueryStringName = "clientid";
        internal const string AzureFunctionsCallbackKey = "MS_AzureFunctionsCallback";

        private readonly Dictionary<string, IWebHookReceiver> _receiverLookup;
        private HttpConfiguration _httpConfiguration;
        private ISecretManager _secretManager;
        private bool disposedValue = false;

        [CLSCompliant(false)]
        public WebHookReceiverManager(ISecretManager secretManager)
        {
            _secretManager = secretManager;
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
            var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            string webHookReceiver = httpTrigger.WebHookType;
            IWebHookReceiver receiver = null;
            if (string.IsNullOrEmpty(webHookReceiver) || !_receiverLookup.TryGetValue(webHookReceiver, out receiver))
            {
                // The function is not correctly configured. Log an error and return 500
                string configurationError = string.Format(CultureInfo.InvariantCulture, "Invalid WebHook configuration. Unable to find a receiver for WebHook type '{0}'", webHookReceiver);
                function.Invoker.OnError(new FunctionInvocationException(configurationError));

                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            }

            // if the code value is specified via header rather than query string
            // promote it to the query string (that's what the WebHook library expects)
            ApplyHeaderValuesToQuery(request);

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

            // Get an optional client ID. This information is passed as the receiver ID, allowing
            // the receiver config to map configuration based on the client ID (primarily used for secret resolution).
            string clientId = GetClientID(request);

            string webhookId = $"{receiverId},{clientId}";

            return await receiver.ReceiveAsync(webhookId, context, request);
        }

        internal static void ApplyHeaderValuesToQuery(HttpRequestMessage request)
        {
            var query = HttpUtility.ParseQueryString(request.RequestUri.Query);
            IEnumerable<string> values = null;
            if (request.Headers.TryGetValues(AuthorizationLevelAttribute.FunctionsKeyHeaderName, out values) &&
                string.IsNullOrEmpty(query.Get("code")))
            {
                string value = values.FirstOrDefault();
                if (!string.IsNullOrEmpty(value))
                {
                    query["code"] = value;
                }

                UriBuilder builder = new UriBuilder(request.RequestUri);
                builder.Query = query.ToString();
                request.RequestUri = builder.Uri;
            }
        }

        private static string GetClientID(HttpRequestMessage request)
        {
            string keyValue = null;
            IEnumerable<string> headerValues;
            if (request.Headers.TryGetValues(FunctionsClientIdHeaderName, out headerValues))
            {
                keyValue = headerValues.FirstOrDefault();
            }
            else
            {
                keyValue = request.GetQueryNameValuePairs()
                      .FirstOrDefault(q => string.Compare(q.Key, FunctionsClientIdQueryStringName, StringComparison.OrdinalIgnoreCase) == 0)
                      .Value;
            }

            return keyValue;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    _httpConfiguration?.Dispose();
                    (_secretManager as IDisposable)?.Dispose();
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
                var requestHandler = (Func<HttpRequestMessage, Task<HttpResponseMessage>>)context.Request.Properties[AzureFunctionsCallbackKey];

                context.Request.Properties.Add(ScriptConstants.AzureFunctionsWebHookContextKey, context);

                // Invoke the function
                context.Response = await requestHandler(context.Request);
            }
        }
    }
}