// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dependencies;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Host;
using Microsoft.Azure.WebJobs.Script.WebHost.Filters;
using Microsoft.Azure.WebJobs.Script.WebHost.Properties;
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
            var request = controllerContext.Request;
            var function = _scriptHostManager.GetHttpFunctionOrNull(request);

            var secretManager = controllerContext.Configuration.DependencyResolver.GetService<ISecretManager>();
            var functionRequestInvoker = new FunctionRequestInvoker(function, secretManager);
            var response = await functionRequestInvoker.PreprocessRequestAsync(request);

            if (response != null)
            {
                return response;
            }

            Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> processRequestHandler = async (req, ct) =>
            {
                return await functionRequestInvoker.ProcessRequestAsync(req, ct, _scriptHostManager, _webHookReceiverManager);
            };

            if (function.Metadata.IsProxy)
            {
                IFuncExecutor proxyFunctionExecutor = new ProxyFunctionExecutor(this._scriptHostManager, _webHookReceiverManager, secretManager);
                request.Properties.Add(ScriptConstants.AzureProxyFunctionExecutorKey, proxyFunctionExecutor);
            }

            return await _scriptHostManager.HttpRequestManager.ProcessRequestAsync(request, processRequestHandler, cancellationToken);
        }
    }
}
