// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    public class FunctionInvocationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILoggerFactory _loggerFactory;

        public FunctionInvocationMiddleware(RequestDelegate next, ILoggerFactory loggerFactory)
        {
            _next = next;
            _loggerFactory = loggerFactory;
        }

        public async Task Invoke(HttpContext context)
        {
            await _next(context);

            IFunctionExecutionFeature functionExecution = context.Features.Get<IFunctionExecutionFeature>();

            if (functionExecution != null && !context.Response.HasStarted)
            {
                IActionResult result = await GetResultAsync(context, functionExecution);

                var actionContext = new ActionContext
                {
                    HttpContext = context
                };

                await result.ExecuteResultAsync(actionContext);
            }
        }

        private async Task<IActionResult> GetResultAsync(HttpContext context, IFunctionExecutionFeature functionExecution)
        {
            if (functionExecution.Descriptor == null)
            {
                return new NotFoundResult();
            }

            // Add route data to request info
            // TODO: Keeping this here for now as other code depend on this property, but this can be done in the HTTP binding.
            var routingFeature = context.Features.Get<IRoutingFeature>();
            context.Items.Add(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, new Dictionary<string, object>(routingFeature.RouteData.Values));

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
                ILogger logger = _loggerFactory.CreateLogger(LogCategories.Function);
                var scopeState = new Dictionary<string, object>()
                {
                    [ScriptConstants.LoggerHttpRequest] = context.Request
                };

                using (logger.BeginScope(scopeState))
                {
                    // TODO: Flow cancellation token from caller
                    await functionExecution.ExecuteAsync(context.Request, CancellationToken.None);
                }
            }

            if (context.Items.TryGetValue(ScriptConstants.AzureFunctionsHttpResponseKey, out object result) && result is IActionResult actionResult)
            {
                return actionResult;
            }

            return new OkResult();
        }

        private async Task<bool> AuthenticateAndAuthorizeAsync(HttpContext context, FunctionDescriptor descriptor)
        {
            var policyEvaluator = context.RequestServices.GetRequiredService<IPolicyEvaluator>();
            AuthorizationPolicy policy = AuthUtility.CreateFunctionPolicy();

            // Authenticate the request
            var authenticateResult = await policyEvaluator.AuthenticateAsync(policy, context);

            // Authorize using the function policy and resource
            var authorizeResult = await policyEvaluator.AuthorizeAsync(policy, authenticateResult, context, descriptor);

            return authorizeResult.Succeeded;
        }
    }
}
