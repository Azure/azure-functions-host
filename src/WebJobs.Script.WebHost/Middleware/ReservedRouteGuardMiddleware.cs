// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Config;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal sealed class ReservedRouteGuardMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IEnvironment _environment;
        private readonly ILogger<ReservedRouteGuardMiddleware> _logger;
        private readonly RequestDelegate _invokeBeforeSpecialization;
        private readonly RequestDelegate _invokeEnforcement;
        private RequestDelegate _invoke;

        public ReservedRouteGuardMiddleware(RequestDelegate next, IEnvironment environment, ILogger<ReservedRouteGuardMiddleware> logger)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(environment);
            ArgumentNullException.ThrowIfNull(logger);

            _next = next;
            _environment = environment;
            _logger = logger;
            _invokeBeforeSpecialization = InvokeBeforeSpecialization;
            _invokeEnforcement = InvokeEnforcement;
            _invoke = _invokeBeforeSpecialization;
        }

        internal RequestDelegate InnerInvoke => _invoke;

        public Task Invoke(HttpContext context)
        {
            return _invoke(context);
        }

        internal Task InvokeBeforeSpecialization(HttpContext context)
        {
            if (_environment.IsPlaceholderModeEnabled())
            {
                return _invokeEnforcement(context);
            }

            bool disabled = FeatureFlags.IsEnabled(ScriptConstants.FeatureFlagDisableReservedRouteEnforcement, _environment);
            RequestDelegate target = disabled ? _next : _invokeEnforcement;
            RequestDelegate previous = Interlocked.CompareExchange(ref _invoke, target, _invokeBeforeSpecialization);

            if (disabled && ReferenceEquals(previous, _invokeBeforeSpecialization))
            {
                _logger.LogInformation(
                    "Reserved route enforcement is disabled by feature flag '{featureFlag}'.",
                    ScriptConstants.FeatureFlagDisableReservedRouteEnforcement);
            }

            return _invoke(context);
        }

        internal Task InvokeEnforcement(HttpContext context)
        {
            if (context.Request.IsReservedRouteRequest() &&
                !(context.Request.IsAdminWarmupRequest() && _environment.IsAdminWarmupRouteEnabled()))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.CompletedTask;
            }

            return _next(context);
        }
    }
}
