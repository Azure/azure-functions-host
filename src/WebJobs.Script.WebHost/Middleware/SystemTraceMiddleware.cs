﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.WebJobs.Script.Extensions;
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

            var start = DateTime.UtcNow;
            var request = context.Request;
            var details = new JObject
            {
                { "requestId", requestId },
                { "method", request.Method },
                { "uri", request.Path.ToString() }
            };
            var logData = new Dictionary<string, object>
            {
                [ScriptConstants.LogPropertyActivityIdKey] = requestId
            };
            _logger.Log(LogLevel.Information, 0, logData, null, (s, e) => $"Executing HTTP request: {details}");

            await _next.Invoke(context);

            var duration = DateTime.UtcNow - start;
            details["identities"] = GetIdentities(context);
            details["status"] = context.Response.StatusCode;
            details["duration"] = duration.TotalMilliseconds;
            _logger.Log(LogLevel.Information, 0, logData, null, (s, e) => $"Executed HTTP request: {details}");
        }

        internal static string SetRequestId(HttpRequest request)
        {
            string requestID = request.GetHeaderValueOrDefault(ScriptConstants.AntaresLogIdHeaderName) ?? Guid.NewGuid().ToString();
            request.HttpContext.Items[ScriptConstants.AzureFunctionsRequestIdKey] = requestID;
            return requestID;
        }

        private static JArray GetIdentities(HttpContext context)
        {
            JArray identities = new JArray();
            foreach (var identity in context.User.Identities.Where(p => p.IsAuthenticated))
            {
                var formattedIdentity = new JObject
                {
                    { "type", identity.AuthenticationType }
                };

                var claim = identity.Claims.FirstOrDefault(p => p.Type == SecurityConstants.AuthLevelClaimType);
                if (claim != null)
                {
                    formattedIdentity.Add("level", claim.Value);
                }

                identities.Add(formattedIdentity);
            }

            return identities;
        }
    }
}
