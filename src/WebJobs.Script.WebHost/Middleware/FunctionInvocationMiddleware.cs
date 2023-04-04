// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Authentication;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class FunctionInvocationMiddleware
    {
        private readonly RequestDelegate _next;

        public FunctionInvocationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        // TODO: Confirm that only HttpTrigger requests would flow through here
        public async Task Invoke(HttpContext context)
        {
            if (_next != null)
            {
                await _next(context);
            }

            var functionExecution = context.Features.Get<IFunctionExecutionFeature>();
            if (functionExecution != null && !context.Response.HasStarted)
            {
                // LiveLogs session id is used to show only contextual logs in the "Code + Test" experience. The id is included in the custom dimension.
                string sessionId = context.Request?.Headers[ScriptConstants.LiveLogsSessionAIKey];
                if (!string.IsNullOrWhiteSpace(sessionId))
                {
                    Activity.Current?.AddBaggage(ScriptConstants.LiveLogsSessionAIKey, sessionId);
                }

                int nestedProxiesCount = GetNestedProxiesCount(context, functionExecution);
                IActionResult result = await GetResultAsync(context, functionExecution);

                if (context.Items.TryGetValue(ScriptConstants.HttpProxyingEnabled, out var value))
                {
                    if (value?.ToString() == bool.TrueString)
                    {
                        return;
                    }
                }

                if (nestedProxiesCount > 0)
                {
                    // if Proxy, the rest of the pipeline will be processed by Proxies in
                    // case there are response overrides and what not.
                    SetProxyResult(context, nestedProxiesCount, result);
                    return;
                }

                ActionContext actionContext = new ActionContext(context, context.GetRouteData(), new ActionDescriptor());
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
                context.Items[ScriptConstants.AzureFunctionsColdStartKey] = ValueStopwatch.StartNew();
            }

            PopulateRouteData(context);

            bool authorized = await AuthenticateAndAuthorizeAsync(context, functionExecution.Descriptor);
            if (!authorized)
            {
                return new UnauthorizedResult();
            }

            // If the function is disabled, return 'NotFound', unless the request is being made with Admin credentials
            if (functionExecution.Descriptor.Metadata.IsDisabled() &&
                !AuthUtility.PrincipalHasAuthLevelClaim(context.User, AuthorizationLevel.Admin))
            {
                return new NotFoundResult();
            }

            if (functionExecution.CanExecute)
            {
                // Add the request to the logging scope. This allows the App Insights logger to
                // record details about the request.
                ILoggerFactory loggerFactory = context.RequestServices.GetService<ILoggerFactory>();
                ILogger logger = loggerFactory.CreateLogger(functionExecution.Descriptor.LogCategory);
                var scopeState = new Dictionary<string, object>()
                {
                    [ScriptConstants.LoggerHttpRequest] = context.Request,
                };

                using (logger.BeginScope(scopeState))
                {
                    var applicationLifetime = context.RequestServices.GetService<IApplicationLifetime>();
                    CancellationToken cancellationToken = context.RequestAborted;

                    await functionExecution.ExecuteAsync(context.Request, cancellationToken);

                    if (context.Items.TryGetValue(ScriptConstants.AzureFunctionsDuplicateHttpHeadersKey, out object value))
                    {
                        logger.LogDebug($"Duplicate HTTP header from function invocation removed. Duplicate key(s): {value?.ToString()}.");
                    }
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
            var routingFeature = context.Features.Get<IRoutingFeature>();

            // Add route data to request info
            // TODO: Keeping this here for now as other code depend on this property, but this can be done in the HTTP binding.
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
            if (RequiresAuthz(context.Request, descriptor))
            {
                // Authenticate the request
                var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();
                var policy = AuthUtility.DefaultFunctionPolicy;
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

        internal static bool RequiresAuthz(HttpRequest request, FunctionDescriptor descriptor)
        {
            if (descriptor.Metadata.IsProxy() || descriptor.IsWarmupFunction())
            {
                return false;
            }

            var httpTrigger = descriptor.HttpTriggerAttribute;
            if (httpTrigger?.AuthLevel == AuthorizationLevel.Anonymous &&
                !request.Headers.ContainsKey(ScriptConstants.EasyAuthIdentityHeader) &&
                !request.Headers.ContainsKey(AuthenticationLevelHandler.FunctionsKeyHeaderName) &&
                !request.Query.ContainsKey(AuthenticationLevelHandler.FunctionsKeyQueryParamName))
            {
                // Anonymous functions w/o any of our special request headers don't require authz.
                // In cases where the function is anonymous but has one of these headers, we run
                // authz so claims are populated.
                return false;
            }

            return true;
        }
    }
}
