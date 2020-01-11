// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class FunctionInvocationMiddleware
    {
        private readonly RequestDelegate _next;
        private IApplicationLifetime _applicationLifetime;

        public FunctionInvocationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            if (_next != null)
            {
                await _next(context);
            }

            _applicationLifetime = context.RequestServices.GetService<IApplicationLifetime>();

            IFunctionExecutionFeature functionExecution = context.Features.Get<IFunctionExecutionFeature>();
            if (functionExecution != null && !context.Response.HasStarted())
            {
                int nestedProxiesCount = GetNestedProxiesCount(context, functionExecution);
                IActionResult result = await GetResultAsync(context, functionExecution);
                if (nestedProxiesCount > 0)
                {
                    // if Proxy, the rest of the pipleline will be processed by Proxies in
                    // case there are response overrides and what not.
                    SetProxyResult(context, nestedProxiesCount, result);
                    return;
                }

                ActionContext actionContext = new ActionContext(context, new RouteData(), new ActionDescriptor());
                await result.ExecuteResultAsync(actionContext);
            }
        }

        private static void SetProxyResult(HttpContext context, int nestedProxiesCount, IActionResult result)
        {
            context.Items[ScriptConstants.AzureFunctionsProxyResult] = result;
            context.Items[ScriptConstants.AzureFunctionsNestedProxyCount] = nestedProxiesCount - 1;
        }

        private static int GetNestedProxiesCount(HttpContext context, IFunctionExecutionFeature functionExecution)
        {
            context.Items.TryGetValue(ScriptConstants.AzureFunctionsNestedProxyCount, out object nestedProxiesCount);

            if (functionExecution != null && !functionExecution.Descriptor.Metadata.IsProxy && nestedProxiesCount == null)
            {
                // HttpBufferingService is disabled for non-proxy functions.
                var bufferingFeature = context.Features.Get<IScriptHttpBufferedStream>();
                bufferingFeature?.DisableBufferingAsync(CancellationToken.None);
            }

            if (nestedProxiesCount != null)
            {
                return (int)nestedProxiesCount;
            }

            return 0;
        }

        private async Task<IActionResult> GetResultAsync(HttpContext context, IFunctionExecutionFeature functionExecution)
        {
            if (functionExecution.Descriptor == null)
            {
                return new NotFoundResult();
            }

            if (context.Request.IsColdStart() && !context.Items.ContainsKey(ScriptConstants.AzureFunctionsColdStartKey))
            {
                // for cold start requests we want to measure the request
                // pipeline dispatch time
                // important that this stopwatch is started as early as possible
                // in the pipeline (in this case, in our first middleware)
                var sw = new Stopwatch();
                sw.Start();
                context.Items[ScriptConstants.AzureFunctionsColdStartKey] = sw;
            }

            PopulateRouteData(context);

            bool authorized = await AuthenticateAndAuthorizeAsync(context, functionExecution.Descriptor);
            if (!authorized)
            {
                return new UnauthorizedResult();
            }

            // If the function is disabled, return 'NotFound', unless the request is being made with Admin credentials
            if (functionExecution.Descriptor.Metadata.IsDisabled &&
                !AuthUtility.PrincipalHasAuthLevelClaim(context.User, AuthorizationLevel.Admin))
            {
                return new NotFoundResult();
            }

            if (functionExecution.CanExecute)
            {
                // Add the request to the logging scope. This allows the App Insights logger to
                // record details about the request.
                ILoggerFactory loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
                ILogger logger = loggerFactory.CreateLogger(LogCategories.CreateFunctionCategory(functionExecution.Descriptor.Name));
                var scopeState = new Dictionary<string, object>()
                {
                    [ScriptConstants.LoggerHttpRequest] = context.Request,
                };

                using (logger.BeginScope(scopeState))
                {
                    CancellationToken cancellationToken = _applicationLifetime != null ? _applicationLifetime.ApplicationStopping : CancellationToken.None;
                    await functionExecution.ExecuteAsync(context.Request, cancellationToken);
                }
            }

            if (context.Items.TryGetValue(ScriptConstants.AzureFunctionsHttpResponseKey, out object result) && result is IActionResult actionResult)
            {
                return actionResult;
            }

            return new OkResult();
        }

        private void PopulateRouteData(HttpContext context)
        {
            // Add route data to request info
            // TODO: Keeping this here for now as other code depend on this property, but this can be done in the HTTP binding.
            var routingFeature = context.Features.Get<IRoutingFeature>();

            var routeData = new Dictionary<string, object>(routingFeature.RouteData.Values);

            // Get optional parameters that were not used and had no default
            Route functionRoute = routingFeature.RouteData.Routers.FirstOrDefault(r => r is Route) as Route;

            if (functionRoute != null)
            {
                var optionalParameters = functionRoute.ParsedTemplate.Parameters.Where(p => p.IsOptional && p.DefaultValue == null);

                foreach (var parameter in optionalParameters)
                {
                    // Make sure we didn't have the parameter in the values dictionary
                    if (!routeData.ContainsKey(parameter.Name))
                    {
                        routeData.Add(parameter.Name, null);
                    }
                }
            }

            context.Items[HttpExtensionConstants.AzureWebJobsHttpRouteDataKey] = routeData;
        }

        private async Task<bool> AuthenticateAndAuthorizeAsync(HttpContext context, FunctionDescriptor descriptor)
        {
            if (!descriptor.Metadata.IsProxy)
            {
                var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();
                AuthorizationPolicy policy = AuthUtility.CreateFunctionPolicy();

                // Authenticate the request
                var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context);

                // Authorize using the function policy and resource
                var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, descriptor);

                return authorizeResult.Succeeded;
            }
            else
            {
                return true;
            }
        }
    }
}
