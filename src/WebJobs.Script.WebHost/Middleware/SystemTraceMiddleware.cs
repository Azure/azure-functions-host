// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal class SystemTraceMiddleware
    {
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;

        public SystemTraceMiddleware(RequestDelegate next, ILogger<SystemTraceMiddleware> logger)
        {
            _logger = logger;
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var requestId = SetRequestId(context.Request);

            var sw = ValueStopwatch.StartNew();
            _logger.ExecutingHttpRequest(requestId, context.Request.Method);

            await _next.Invoke(context);

            string identities = GetIdentities(context);
            _logger.ExecutedHttpRequest(requestId, identities, context.Response.StatusCode, (long)sw.GetElapsedTime().TotalMilliseconds, GetRouteTemplate(context));
        }

        /// <summary>
        /// Gets the matched route template for the request, e.g. "api/products/{category}/{id}".
        /// </summary>
        /// <remarks>
        /// Routing has not run yet when the request starts, so this is only meaningful once the rest of the
        /// pipeline has completed. Requests that were never routed (404s, static files, requests rejected earlier
        /// in the pipeline) have no matched route; those return <see cref="string.Empty"/> rather than falling back
        /// to the raw path, which would reintroduce the caller supplied values this log deliberately omits.
        /// </remarks>
        internal static string GetRouteTemplate(HttpContext context)
        {
            var routingFeature = context.Features.Get<IRoutingFeature>();
            if (routingFeature is null)
            {
                return string.Empty;
            }

            var route = routingFeature.RouteData?.Routers.FirstOrDefault(r => r is Route) as Route;

            return route?.RouteTemplate ?? string.Empty;
        }

        internal static string SetRequestId(HttpRequest request)
        {
            string requestID = request.GetHeaderValueOrDefault(ScriptConstants.AntaresLogIdHeaderName) ?? Guid.NewGuid().ToString();
            request.HttpContext.Items[ScriptConstants.AzureFunctionsRequestIdKey] = requestID;
            return requestID;
        }

        private static string GetIdentities(HttpContext context)
        {
            var identities = context.User.Identities.Where(p => p.IsAuthenticated);
            if (identities.Any())
            {
                var sbIdentities = new StringBuilder();

                foreach (var identity in identities)
                {
                    if (sbIdentities.Length > 0)
                    {
                        sbIdentities.Append(", ");
                    }

                    var sbIdentity = new StringBuilder(identity.AuthenticationType);
                    var claim = identity.Claims.FirstOrDefault(p => p.Type == SecurityConstants.AuthLevelClaimType);
                    if (claim != null)
                    {
                        sbIdentity.Append(':');
                        sbIdentity.Append(claim.Value);
                    }

                    sbIdentities.Append(sbIdentity);
                }

                sbIdentities.Insert(0, '(');
                sbIdentities.Append(')');

                return sbIdentities.ToString();
            }
            else
            {
                return string.Empty;
            }
        }
    }
}