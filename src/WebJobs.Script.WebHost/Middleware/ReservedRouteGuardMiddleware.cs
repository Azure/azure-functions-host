// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal sealed class ReservedRouteGuardMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IOptionsMonitor<StandbyOptions> _standbyOptions;
        private readonly IOptionsMonitor<ReservedRouteOptions> _reservedRouteOptions;
        private readonly ILogger<ReservedRouteGuardMiddleware> _logger;
        private readonly RequestDelegate _invokeBeforeSpecialization;
        private readonly RequestDelegate _invokeEnforcement;
        private RequestDelegate _invoke;

        public ReservedRouteGuardMiddleware(
            RequestDelegate next,
            IOptionsMonitor<StandbyOptions> standbyOptions,
            IOptionsMonitor<ReservedRouteOptions> reservedRouteOptions,
            ILogger<ReservedRouteGuardMiddleware> logger)
        {
            ArgumentNullException.ThrowIfNull(next);
            ArgumentNullException.ThrowIfNull(standbyOptions);
            ArgumentNullException.ThrowIfNull(reservedRouteOptions);
            ArgumentNullException.ThrowIfNull(logger);

            _next = next;
            _standbyOptions = standbyOptions;
            _reservedRouteOptions = reservedRouteOptions;
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
            if (_standbyOptions.CurrentValue.InStandbyMode)
            {
                return _invokeEnforcement(context);
            }

            bool disabled = _reservedRouteOptions.CurrentValue.DisableReservedRouteEnforcement;
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
                !(context.Request.IsAdminWarmupRequest() && _reservedRouteOptions.CurrentValue.AdminWarmupRouteEnabled))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                return Task.CompletedTask;
            }

            return _next(context);
        }
    }
}
