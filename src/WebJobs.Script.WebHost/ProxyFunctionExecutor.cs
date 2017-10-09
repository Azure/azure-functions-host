// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.AppService.Proxy.Client.Contract;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.WebHost;

namespace Microsoft.Azure.WebJobs.Script.Host
{
    public class ProxyFunctionExecutor : IFuncExecutor
    {
        private readonly WebScriptHostManager _scriptHostManager;
        private readonly IWebJobsRouteHandler _routeHandler;

        internal ProxyFunctionExecutor(WebScriptHostManager scriptHostManager, IWebJobsRouteHandler routeHandler)
        {
            _scriptHostManager = scriptHostManager;
            _routeHandler = routeHandler;
        }

        public async Task ExecuteFuncAsync(string functionName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            var request = arguments[ScriptConstants.AzureFunctionsHttpRequestKey] as HttpRequest;
            var function = _scriptHostManager.Instance.Functions.FirstOrDefault(f => string.Equals(f.Name, functionName));

            await _routeHandler.InvokeAsync(request.HttpContext, functionName);
        }
    }
}
