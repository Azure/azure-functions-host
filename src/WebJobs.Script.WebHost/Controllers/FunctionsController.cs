// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Security;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Controllers
{
    /// <summary>
    /// Controller responsible for handling all http function invocations.
    /// </summary>
    public class FunctionsController : ApiController
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly WebHookReceiverManager _webHookReceiverManager;
        private readonly ScriptSettingsManager _settingsManager;

        public FunctionsController(WebScriptHostManager scriptHostManager, WebHookReceiverManager webHookReceiverManager, ScriptSettingsManager settingsManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHookReceiverManager = webHookReceiverManager;
            _settingsManager = settingsManager;
        }

        public override async Task<HttpResponseMessage> ExecuteAsync(HttpControllerContext controllerContext, CancellationToken cancellationToken)
        {
            var request = controllerContext.Request;
            var function = _scriptHostManager.GetHttpFunctionOrNull(request);
            if (function == null)
            {
                // request does not map to an HTTP function
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            request.SetProperty(ScriptConstants.AzureFunctionsHttpFunctionKey, function);

            var authorizationLevel = await DetermineAuthorizationLevelAsync(request, function, _settingsManager, controllerContext.Configuration.DependencyResolver);
            if (function.Metadata.IsExcluded ||
               (function.Metadata.IsDisabled && !(request.IsAuthDisabled() || authorizationLevel == AuthorizationLevel.Admin)))
            {
                // disabled functions are not publicly addressable w/o Admin level auth,
                // and excluded functions are also ignored here (though the check above will
                // already exclude them)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> processRequestHandler = async (req, ct) =>
            {
                return await ProcessRequestAsync(req, function, ct);
            };
            return await _scriptHostManager.HttpRequestManager.ProcessRequestAsync(request, processRequestHandler, cancellationToken);
        }

        public static async Task<AuthorizationLevel> DetermineAuthorizationLevelAsync(HttpRequestMessage request, FunctionDescriptor function, ScriptSettingsManager settingsManager, IDependencyResolver resolver)
        {
            // first check if we're authorized via key
            var secretManager = resolver.GetService<ISecretManager>();
            var authorizationResult = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, secretManager, functionName: function.Name);
            var authorizationLevel = authorizationResult.AuthorizationLevel;

            if (authorizationLevel > AuthorizationLevel.Anonymous)
            {
                // add a new identity to the principal representing
                // the key authentication result
                AddKeyAuthorizationIdentity(ClaimsPrincipal.Current, authorizationResult);
            }

            // See if we're we're authorized at the User level (EasyAuth)
            // and apply the result (only if we're not also authorized already
            // at a higher level)
            if (authorizationLevel < AuthorizationLevel.User &&
                HasEasyAuthIdentity(request, settingsManager))
            {
                authorizationLevel = AuthorizationLevel.User;
            }

            request.SetAuthorizationLevel(authorizationLevel);
            request.SetProperty(ScriptConstants.AzureFunctionsHttpRequestKeyIdKey, authorizationResult.KeyId);

            return authorizationLevel;
        }

        internal static void AddKeyAuthorizationIdentity(ClaimsPrincipal principal, KeyAuthorizationResult result)
        {
            var identity = new ClaimsIdentity(ScriptConstants.AzureFunctionsKeyAuthenticationType);
            identity.AddClaim(new Claim(ScriptConstants.AzureFunctionsAuthLevelClaimName, result.AuthorizationLevel.ToString()));
            identity.AddClaim(new Claim(ScriptConstants.AzureFunctionsKeyIdClaimName, result.KeyId));

            principal.AddIdentity(identity);
        }

        internal static bool HasEasyAuthIdentity(HttpRequestMessage request, ScriptSettingsManager settingsManager)
        {
            if (settingsManager.IsAppServiceEnvironment)
            {
                // Note: this special header IS NOT spoofable by external clients and is a secure
                // indicator (when running in AppService) that the current request is EasyAuth
                // authenticated. Only check this if we're running under AppService.
                var easyAuthIdentityProvider = request.GetHeaderValueOrDefault(ScriptConstants.AntaresEasyAuthProviderHeaderName);
                return !string.IsNullOrEmpty(easyAuthIdentityProvider);
            }

            return false;
        }

        private async Task<HttpResponseMessage> ProcessRequestAsync(HttpRequestMessage request, FunctionDescriptor function, CancellationToken cancellationToken)
        {
            var httpTrigger = function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            bool isWebHook = !string.IsNullOrEmpty(httpTrigger.WebHookType);
            var authorizationLevel = request.GetAuthorizationLevel();
            HttpResponseMessage response = null;

            if (isWebHook)
            {
                if (request.HasAuthorizationLevel(AuthorizationLevel.Admin))
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
                if (!request.HasAuthorizationLevel(httpTrigger.AuthLevel))
                {
                    return new HttpResponseMessage(HttpStatusCode.Unauthorized);
                }

                // Not a WebHook request so dispatch directly
                response = await _scriptHostManager.HandleRequestAsync(function, request, cancellationToken);
            }

            return response;
        }
    }
}
