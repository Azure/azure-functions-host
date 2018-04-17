// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Dependencies;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public class FunctionRequestInvoker
    {
        private readonly ISecretManager _secretManager;
        private readonly FunctionDescriptor _function;

        public FunctionRequestInvoker(FunctionDescriptor function, ISecretManager secretManager)
        {
            _secretManager = secretManager;
            _function = function;
        }

        private static bool IsHomepageDisabled
        {
            get
            {
                return string.Equals(Environment.GetEnvironmentVariable(EnvironmentSettingNames.AzureWebJobsDisableHomepage),
                    bool.TrueString, StringComparison.OrdinalIgnoreCase);
            }
        }

        public async Task<HttpResponseMessage> ProcessRequestAsync(HttpRequestMessage request, CancellationToken cancellationToken, WebScriptHostManager scriptHostManager, WebHookReceiverManager webHookReceiverManager)
        {
            var httpTrigger = _function.GetTriggerAttributeOrNull<HttpTriggerAttribute>();
            bool isWebHook = !string.IsNullOrEmpty(httpTrigger.WebHookType);
            var authorizationLevel = request.GetAuthorizationLevel();
            HttpResponseMessage response = null;

            if (isWebHook)
            {
                if (request.HasAuthorizationLevel(AuthorizationLevel.Admin))
                {
                    // Admin level requests bypass the WebHook auth pipeline
                    response = await scriptHostManager.HandleRequestAsync(_function, request, cancellationToken);
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

                        return await scriptHostManager.HandleRequestAsync(_function, req, cancellationToken);
                    };
                    response = await webHookReceiverManager.HandleRequestAsync(_function, request, invokeFunction);
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
                response = await scriptHostManager.HandleRequestAsync(_function, request, cancellationToken);
            }

            return response;
        }

        public async Task<HttpResponseMessage> PreprocessRequestAsync(HttpRequestMessage request)
        {
            if (_function == null)
            {
                if (request.RequestUri.AbsolutePath == "/")
                {
                    // if the request is to the root and we can't find any matching FunctionDescriptors which might have been setup by proxies
                    // then homepage logic will be applied.
                    return (IsHomepageDisabled || request.IsAntaresInternalRequest())
                        ? new HttpResponseMessage(HttpStatusCode.NoContent)
                        : new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(Resources.Homepage, Encoding.UTF8, "text/html")
                        };
                }

                // request does not map to an HTTP function
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            request.SetProperty(ScriptConstants.AzureFunctionsHttpFunctionKey, _function);

            var authorizationLevel = await DetermineAuthorizationLevelAsync(request);
            if (_function.Metadata.IsDisabled && !(request.IsAuthDisabled() || authorizationLevel == AuthorizationLevel.Admin))
            {
                // disabled functions are not publicly addressable w/o Admin level auth,
                // and excluded functions are also ignored here
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }

            return null;
        }

        private async Task<AuthorizationLevel> DetermineAuthorizationLevelAsync(HttpRequestMessage request)
        {
            AuthorizationLevel authorizationLevel = AuthorizationLevel.Anonymous;

            if (_function.Metadata.IsProxy)
            {
                // There is no authorization for proxies. Bypass the logic that probes
                // for secrets using the secret manager and just return anonymous:
                authorizationLevel = AuthorizationLevel.Anonymous;
            }
            else
            {
                var authorizationResult = await AuthorizationLevelAttribute.GetAuthorizationResultAsync(request, _secretManager, functionName: _function.Name);
                authorizationLevel = authorizationResult.AuthorizationLevel;
                request.SetProperty(ScriptConstants.AzureFunctionsHttpRequestKeyNameKey, authorizationResult.KeyName);
            }

            request.SetAuthorizationLevel(authorizationLevel);

            return authorizationLevel;
        }
    }
}
