// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs.Logging;
using Microsoft.Azure.WebJobs.Script;
using Microsoft.Azure.WebJobs.Script.Description;
using Microsoft.Azure.WebJobs.Script.WebHost.Features;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.Azure.WebJobs.Extensions.Http
{
    public class ScriptRouteHandler : IWebJobsRouteHandler
    {
        private readonly Func<ScriptHost> _scriptHostProvider;
        private readonly ILoggerFactory _loggerFactory;

        public ScriptRouteHandler(ILoggerFactory loggerFactory, Func<ScriptHost> scriptHostProvider)
        {
            _scriptHostProvider = scriptHostProvider;
            _loggerFactory = loggerFactory;
        }

        public ScriptRouteHandler(ILoggerFactory loggerFactory, ScriptHost scriptHost)
        {
            _loggerFactory = loggerFactory;
            _scriptHostProvider = () => scriptHost;
        }

        public async Task InvokeAsync(HttpContext context, string functionName)
        {
            IActionResult result = await GetResultAsync(context, functionName);

            var actionContext = new ActionContext
            {
                HttpContext = context
            };

            await result.ExecuteResultAsync(actionContext);
        }

        private async Task<IActionResult> GetResultAsync(HttpContext context, string functionName)
        {
            ScriptHost host = _scriptHostProvider();

            // TODO: FACAVAL This should be improved....
            FunctionDescriptor descriptor = host.Functions.FirstOrDefault(f => string.Equals(f.Name, functionName));

            if (descriptor == null)
            {
                return new NotFoundResult();
            }

            var routingFeature = context.Features.Get<IRoutingFeature>();

            // Add rounte data to request info
            // TODO: Keeping this here for now as other code depend on this property, but this can be done in the HTTP binding.
            context.Items.Add(HttpExtensionConstants.AzureWebJobsHttpRouteDataKey, new Dictionary<string,object>(routingFeature.RouteData.Values));

            context.Features.Set<IFunctionExecutionFeature>(new FunctionExecutionFeature { Descriptor = descriptor });

            bool authorized = await AuthenticateAndAuthorizeAsync(context, descriptor);

            if (!authorized)
            {
                return new UnauthorizedResult();
            }

            Dictionary<string, object> arguments = GetFunctionArguments(descriptor, context.Request);

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
                await host.CallAsync(descriptor.Name, arguments, CancellationToken.None);
            }

            if (context.Items.TryGetValue(ScriptConstants.AzureFunctionsHttpResponseKey, out object result) && result is IActionResult actionResult)
            {
                return actionResult;
            }

            return new NotFoundResult();
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

        private static Dictionary<string, object> GetFunctionArguments(FunctionDescriptor function, HttpRequest request)
        {
            ParameterDescriptor triggerParameter = function.Parameters.First(p => p.IsTrigger);
            Dictionary<string, object> arguments = new Dictionary<string, object>();

            if (triggerParameter.Type != typeof(HttpRequest))
            {
                // see if the function defines a parameter to receive the HttpRequestMessage and
                // if so, pass it along
                ParameterDescriptor requestParameter = function.Parameters.FirstOrDefault(p => p.Type == typeof(HttpRequest));
                if (requestParameter != null)
                {
                    arguments.Add(requestParameter.Name, request);
                }
            }

            arguments.Add(triggerParameter.Name, request);

            return arguments;
        }
    }
}