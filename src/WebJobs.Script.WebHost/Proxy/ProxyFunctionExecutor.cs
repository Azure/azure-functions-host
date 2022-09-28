// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.AppService.Proxy.Client;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Middleware;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Proxy
{
    public class ProxyFunctionExecutor : IFuncExecutor
    {
        private readonly IScriptJobHost _scriptHost;

        internal ProxyFunctionExecutor(IScriptJobHost scriptHost)
        {
            _scriptHost = scriptHost;
        }

        public async Task<IActionResult> ExecuteFuncAsync(string functionName, Dictionary<string, object> arguments, CancellationToken cancellationToken)
        {
            var request = arguments[ScriptConstants.AzureFunctionsHttpRequestKey] as HttpRequest;

            if (CheckForInfiniteLoop(request))
            {
                return new BadRequestObjectResult("Infinite loop detected when trying to call a local function or proxy from a proxy.");
            }

            var httpContext = request.HttpContext;

            httpContext.Items.Add(HttpExtensionConstants.AzureWebJobsUseReverseRoutesKey, true);

            var route = httpContext.GetRouteData();

            RouteContext rc = new RouteContext(httpContext);

            foreach (var router in route.Routers)
            {
                if (router is WebJobsRouter webJobsRouter)
                {
                    await webJobsRouter.RouteAsync(rc);

                    if (rc.Handler != null)
                    {
                        break;
                    }
                }
            }

            httpContext.Items.Remove(HttpExtensionConstants.AzureWebJobsUseReverseRoutesKey);
            httpContext.Items.Remove(ScriptConstants.AzureFunctionsHostKey);

            if (rc.Handler == null)
            {
                return new NotFoundResult();
            }

            await rc.Handler.Invoke(httpContext);

            FunctionInvocationMiddleware functionInvocationMiddleware = new FunctionInvocationMiddleware(null);

            if (!httpContext.Items.TryGetValue(ScriptConstants.AzureFunctionsNestedProxyCount, out object nestedProxiesCount))
            {
                httpContext.Items.Add(ScriptConstants.AzureFunctionsNestedProxyCount, 1);
            }
            else
            {
                httpContext.Items[ScriptConstants.AzureFunctionsNestedProxyCount] = (int)nestedProxiesCount + 1;
            }

            await functionInvocationMiddleware.Invoke(httpContext);

            var result = (IActionResult)httpContext.Items[ScriptConstants.AzureFunctionsProxyResult];

            httpContext.Items.Remove(ScriptConstants.AzureFunctionsProxyResult);

            return result;
        }

        private bool CheckForInfiniteLoop(HttpRequest request)
        {
            var httpContext = request.HttpContext;

            // Local Function calls do not go thru ARR, so implementing the ARR's MAX-FORWARDs header logic here to avoid infinte redirects.
            object values;
            int redirectCount = 0;
            if (httpContext.Items.TryGetValue(ScriptConstants.AzureProxyFunctionLocalRedirectKey, out values))
            {
                redirectCount = (int)values;

                if (redirectCount >= ScriptConstants.AzureProxyFunctionMaxLocalRedirects)
                {
                    return true;
                }
            }
            else
            {
                httpContext.Items.Add(ScriptConstants.AzureProxyFunctionLocalRedirectKey, redirectCount);
                return false;
            }

            redirectCount++;
            httpContext.Items[ScriptConstants.AzureProxyFunctionLocalRedirectKey] = redirectCount;

            return false;
        }
    }
}
