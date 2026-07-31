// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal sealed class ReservedRouteGuardMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IOptionsMonitor<ReservedRouteOptions> _reservedRouteOptions;
        private readonly IWebJobsRouter _router;
        private readonly IScriptHostManager _scriptHostManager;
        private readonly ConcurrentDictionary<string, byte> _reportedFunctionRouteCollisions = new(StringComparer.OrdinalIgnoreCase);
        private int _disablementLogAttempted;

        public ReservedRouteGuardMiddleware(RequestDelegate next, IOptionsMonitor<StandbyOptions> standbyOptions,
            IOptionsMonitor<ReservedRouteOptions> reservedRouteOptions, IWebJobsRouter router, IScriptHostManager scriptHostManager)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(standbyOptions);
            ArgumentNullException.ThrowIfNull(reservedRouteOptions);
            ArgumentNullException.ThrowIfNull(router);
            ArgumentNullException.ThrowIfNull(scriptHostManager);

            _next = next;
            _standbyOptions = standbyOptions;
            _reservedRouteOptions = reservedRouteOptions;
            _router = router;
            _scriptHostManager = scriptHostManager;
        }

        public Task Invoke(HttpContext context)
        {
            if (!context.Request.IsReservedRouteRequest())
            {
                return _next(context);
            }

            bool inStandbyMode = _standbyOptions.CurrentValue.InStandbyMode;
            ReservedRouteOptions options = _reservedRouteOptions.CurrentValue;

            if (!inStandbyMode && options.DisableReservedRouteEnforcement)
            {
                if (Interlocked.Exchange(ref _disablementLogAttempted, 1) == 0)
                {
                    TryLog(context, logger => logger.LogInformation("Reserved route enforcement is disabled by feature flag '{featureFlag}'.",
                        ScriptConstants.FeatureFlagDisableReservedRouteEnforcement));
                }

                return _next(context);
            }

            if (context.Request.IsExactAdminWarmupRequest() && options.AdminWarmupRouteEnabled)
            {
                return _next(context);
            }

            return RejectReservedRouteAsync(context, inStandbyMode, options.AdminWarmupRouteEnabled);
        }

        private async Task RejectReservedRouteAsync(HttpContext context, bool inStandbyMode, bool adminWarmupRouteEnabled)
        {
            context.Response.StatusCode = (int)HttpStatusCode.NotFound;

            if (inStandbyMode
                || _scriptHostManager.State != ScriptHostState.Running
                || (adminWarmupRouteEnabled && context.Request.IsAdminWarmupRouteMatch()))
            {
                return;
            }

            string functionName;
            try
            {
                functionName = await GetMatchingFunctionNameAsync(context);
            }
            catch (Exception exception)
            {
                TryLog(context, logger => logger.LogWarning(
                    exception,
                    "An error occurred while checking whether the rejected request path '{requestPath}' matches a function route.",
                    context.Request.Path.Value));

                return;
            }

            if (functionName is not null && _reportedFunctionRouteCollisions.TryAdd(functionName, 0))
            {
                TryLog(context, logger => logger.LogError(
                    "The request path '{requestPath}' was rejected because it uses a reserved host route and matches the route for function '{functionName}'. Update the function route to use a non-reserved path.",
                    context.Request.Path.Value, functionName));
            }
        }

        private async Task<string> GetMatchingFunctionNameAsync(HttpContext context)
        {
            // Use the active router so route prefixes, constraints, and ordering remain the single source of truth.
            var routeContext = new RouteContext(context);
            await _router.RouteAsync(routeContext);

            if (routeContext.Handler is not null
                && routeContext.RouteData.DataTokens.TryGetValue(HttpExtensionConstants.FunctionNameRouteTokenKey, out object value)
                && value is string functionName
                && !string.IsNullOrEmpty(functionName))
            {
                return functionName;
            }

            return null;
        }

        private static void TryLog(HttpContext context, Action<ILogger<ReservedRouteGuardMiddleware>> log)
        {
            try
            {
                ILogger<ReservedRouteGuardMiddleware> logger = context.RequestServices
                    .GetService<ILogger<ReservedRouteGuardMiddleware>>();

                if (logger is not null)
                {
                    log(logger);
                }
            }
            catch (Exception)
            {
                // Diagnostics must not change the reserved-route response.
            }
        }
    }
}
