// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Diagnostics;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Middleware
{
    internal class SystemTraceMiddleware
    {
        // This is double the amount of memory allocated during cold start specialization.
        // This value is calculated based on prod profiles across all languages observed for an extended period of time.
        // This value is just a best effort and if for any reason CLR needs to allocate more memory then it will ignore this value.
        private const long AllocationBudgetForGCDuringSpecialization = 16 * 1024 * 1024;
        private readonly ILogger _logger;
        private readonly RequestDelegate _next;
        private readonly IScriptWebHostEnvironment _webHostEnvironment;

        public SystemTraceMiddleware(RequestDelegate next, IScriptWebHostEnvironment webHostEnvironment, ILogger<SystemTraceMiddleware> logger)
        {
            _webHostEnvironment = webHostEnvironment;
            _logger = logger;
            _next = next;
        }

        public async Task Invoke(HttpContext context)
        {
            var requestId = SetRequestId(context.Request);

            var sw = ValueStopwatch.StartNew();
            string userAgent = context.Request.GetHeaderValueOrDefault("User-Agent");
            _logger.ExecutingHttpRequest(requestId, context.Request.Method, userAgent, context.Request.Path);

            await _next.Invoke(context);

            string identities = GetIdentities(context);
            _logger.ExecutedHttpRequest(requestId, identities, context.Response.StatusCode, (long)sw.GetElapsedTime().TotalMilliseconds);

            // We are tweaking GC behavior in SystemTraceMiddleware as this is one of the last call stacks that get executed during standby mode as well as function exection.
            // We force a GC and enter no GC region in standby mode and exit no GC region after first function execution during specialization.
            StartStopGCAsBestEffort();
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

        private void StartStopGCAsBestEffort()
        {
            try
            {
                if (_webHostEnvironment.InStandbyMode)
                {
                    // This is just to make sure we do not enter NoGCRegion multiple times during standby mode.
                    if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion)
                    {
                        // In standby mode, we enforce a GC then enter NoGCRegion mode as best effort.
                        // This is to try to avoid GC during cold start specialization.
                        GC.Collect();
                        if (!GC.TryStartNoGCRegion(AllocationBudgetForGCDuringSpecialization, disallowFullBlockingGC: false))
                        {
                            _logger.LogError($"CLR runtime failed to commit the requested amount of memory: {AllocationBudgetForGCDuringSpecialization}");
                        }
                        _logger.LogInformation($"Collection count for gen 0: {GC.CollectionCount(0)}, gen 1: {GC.CollectionCount(1)}, gen 2: {GC.CollectionCount(2)}");
                    }
                }
                else
                {
                    // if not in standby mode and we are in NoGCRegion then we end NoGCRegion.
                    if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
                    {
                        GC.EndNoGCRegion();
                        _logger.LogInformation($"Collection count for gen 0: {GC.CollectionCount(0)}, gen 1: {GC.CollectionCount(1)}, gen 2: {GC.CollectionCount(2)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.Message);
            }
        }
    }
}