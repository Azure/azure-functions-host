// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DryIoc;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.Extensions;
using Microsoft.Azure.WebJobs.Script.WebHost.Security.Authentication;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

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

            var sw = Stopwatch.StartNew();
            string userAgent = context.Request.GetHeaderValueOrDefault("User-Agent");
            _logger.ExecutingHttpRequest(requestId, context.Request.Method, userAgent, context.Request.Path);

            await _next.Invoke(context);

            sw.Stop();
            string identities = GetIdentities(context);
            _logger.ExecutedHttpRequest(requestId, identities, context.Response.StatusCode, sw.ElapsedMilliseconds);
        }

        internal static string SetRequestId(HttpRequest request)
        {
            string requestID = request.GetHeaderValueOrDefault(ScriptConstants.AntaresLogIdHeaderName) ?? Guid.NewGuid().ToString();
            request.HttpContext.Items[ScriptConstants.AzureFunctionsRequestIdKey] = requestID;
            return requestID;
        }

        private static string GetIdentities(HttpContext context)
        {
            var sbIdentities = new StringBuilder();

            var identities = context.User.Identities.Where(p => p.IsAuthenticated);
            if (identities.Any())
            {
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
                        sbIdentity.AppendFormat(":{0}", claim.Value);
                    }

                    sbIdentities.Append(sbIdentity);
                }

                sbIdentities.Insert(0, "(");
                sbIdentities.Append(")");
            }

            return sbIdentities.ToString();
        }
    }
}
