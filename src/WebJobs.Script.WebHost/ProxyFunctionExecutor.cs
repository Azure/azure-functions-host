// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http.Dependencies;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost;
using Microsoft.Azure.WebJobs.Script.WebHost.Controllers;
using Microsoft.Azure.WebJobs.Script.WebHost.WebHooks;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public class ProxyFunctionExecutor : IFuncExecutor
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly ISecretManager _secretManager;
        private readonly string _httpPrefix;

        private WebHookReceiverManager _webHookReceiverManager;

        internal ProxyFunctionExecutor(WebScriptHostManager scriptHostManager, WebHookReceiverManager webHookReceiverManager, ISecretManager secretManager)
        {
            _scriptHostManager = scriptHostManager;
            _webHookReceiverManager = webHookReceiverManager;
            _secretManager = secretManager;

            _httpPrefix = HttpExtensionConstants.DefaultRoutePrefix;

            if (_scriptHostManager.Instance != null)
            {
                var json = _scriptHostManager.Instance.ScriptConfig.HostConfig.HostConfigMetadata;

                if (json != null && json["http"] != null && json["http"]["routePrefix"] != null)
                {
                    _httpPrefix = json["http"]["routePrefix"].ToString().Trim(new char[] { '/' });
                }
            }
        }

        public async Task ExecuteFuncAsync(string funcName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            HttpRequestMessage request = arguments[ScriptConstants.AzureFunctionsHttpRequestKey] as HttpRequestMessage;

            FunctionDescriptor function = null;
            var path = request.RequestUri.AbsolutePath.Trim(new char[] { '/' });

            if (path.StartsWith(_httpPrefix))
            {
                path = path.Remove(0, _httpPrefix.Length);
            }

            path = path.Trim(new char[] { '/' });

            // This is a call to the local function app, before handing the route to asp.net to pick the FunctionDescriptor the following will be run:
            // 1. If the request maps to a local http trigger function name then that function will be picked.
            // 2. Else if the request maps to a custom route of a local http trigger function then that function will be picked
            // 3. Otherwise the request will be given to asp.net to pick the appropriate route.
            foreach (var func in _scriptHostManager.HttpFunctions.Values)
            {
                if (!func.Metadata.IsProxy)
                {
                    if (path.Equals(func.Metadata.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        function = func;
                        break;
                    }
                    else
                    {
                        foreach (var binding in func.InputBindings)
                        {
                            if (binding.Metadata.IsTrigger)
                            {
                                string functionRoute = null;
                                var jsonContent = binding.Metadata.Raw;
                                if (jsonContent != null && jsonContent["route"] != null)
                                {
                                    functionRoute = jsonContent["route"].ToString();
                                }

                                // BUG: Known issue, This does not work on dynamic routes like products/{category:alpha}/{id:int?}
                                if (!string.IsNullOrEmpty(functionRoute) && path.Equals(functionRoute, StringComparison.OrdinalIgnoreCase))
                                {
                                    function = func;
                                    break;
                                }
                            }
                        }

                        if (function != null)
                        {
                            break;
                        }
                    }
                }
            }

            if (function == null)
            {
                function = _scriptHostManager.GetHttpFunctionOrNull(request);
            }

            var functionRequestInvoker = new FunctionRequestInvoker(function, _secretManager);
            var response = await functionRequestInvoker.PreprocessRequestAsync(request);

            if (response != null)
            {
                request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = response;
                return;
            }

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> processRequestHandler = async (req, ct) =>
            {
                return await functionRequestInvoker.ProcessRequestAsync(req, ct, _scriptHostManager, _webHookReceiverManager);
            };

            var resp = await _scriptHostManager.HttpRequestManager.ProcessRequestAsync(request, processRequestHandler, cancellationToken);
            request.Properties[ScriptConstants.AzureFunctionsHttpResponseKey] = resp;
            return;
        }
    }
}
