// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all http function invocations.
    /// </summary>
    public class FunctionsController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private WebHookReceiverManager _webHookReceiverManager;

        public FunctionsController(WebScriptHostManager scriptHostManager, WebHookReceiverManager webHookReceiverManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHookReceiverManager = webHookReceiverManager;
        }

        public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            HttpRequestMessage request = controllerContext.Request;

            // First see if the request maps to an HTTP function
            FunctionDescriptor function = _scriptHostManager.GetHttpFunctionOrNull(request.RequestUri);
            if (function == null)
            {
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Determine the authorization level of the request
            SecretManager secretManager = (SecretManager)controllerContext.Configuration.DependencyResolver.GetService(typeof(SecretManager));
            AuthorizationLevel authorizationLevel = AuthorizationLevelAttribute.GetAuthorizationLevel(request, secretManager, functionName: function.Name);

            if (function.Metadata.IsDisabled && 
                authorizationLevel != AuthorizationLevel.Admin)
            {
                // disabled functions are not publically addressable w/o Admin level auth
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            // Dispatch the request
            HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => string.Compare("HttpTrigger", p.Type, StringComparison.OrdinalIgnoreCase) == 0);
            bool isWebHook = !string.IsNullOrEmpty(httpFunctionMetadata.WebHookType);
            HttpResponseMessage response = null;
            if (isWebHook)
            {
                if (authorizationLevel == AuthorizationLevel.Admin)
                {
                    // Admin level requests bypass the WebHook auth pipeline
                    response = await _scriptHostManager.HandleRequestAsync(function, request, cancellationToken);
                }
                else
                {
                    // This is a WebHook request so define a delegate for the user function.
                    // The WebHook Receiver pipeline will first validate the request fully
                    // then invoke this callback.
                    Func<HttpRequestMessage, Task<HttpResponseMessage>> invokeFunction = async (req) =>
                    {
                        // Reset the content stream before passing the request down to the function
                        Stream stream = await req.Content.ReadAsStreamAsync();
                        stream.Seek(0, SeekOrigin.Begin);

                        return await _scriptHostManager.HandleRequestAsync(function, req, cancellationToken);
                    };
                    response = await _webHookReceiverManager.HandleRequestAsync(function, request, invokeFunction);
                }
            }
            else
            {
                // Authorize
                if (authorizationLevel < httpFunctionMetadata.AuthLevel)
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                // Validate the HttpMethod
                // Note that for WebHook requests, WebHook receiver does its own validation
                if (httpFunctionMetadata.Methods != null && !httpFunctionMetadata.Methods.Contains(request.Method))
                {
                    return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);
                }

                // Not a WebHook request so dispatch directly
                response = await _scriptHostManager.HandleRequestAsync(function, request, cancellationToken);
            }
            
            return response;
        }
    }
}
