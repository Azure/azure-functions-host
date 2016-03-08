﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using WebJobs.Script.WebHost.Filters;
using WebJobs.Script.WebHost.WebHooks;

namespace WebJobs.Script.WebHost.Controllers
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

            // Dispatch the request
            HttpTriggerBindingMetadata httpFunctionMetadata = (HttpTriggerBindingMetadata)function.Metadata.InputBindings.FirstOrDefault(p => p.Type == BindingType.HttpTrigger);
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
