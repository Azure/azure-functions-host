// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks
{
    public class HealthCheckResponseWriter
    {
        public static Task WriteResponseAsync(HttpContext httpContext, HealthReport report)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(report);

            // We will write a detailed report if ?expand=true is present.
            if (HealthCheckHelpers.IsExpandedHealthCheck(httpContext.Request))
            {
                return UIResponseWriter.WriteHealthCheckUIResponse(httpContext, report);
            }

            return WriteMinimalResponseAsync(httpContext, report);
        }

        private static Task WriteMinimalResponseAsync(HttpContext httpContext, HealthReport report)
        {
            MinimalResponse body = new(report.Status);
            return JsonSerializer.SerializeAsync(
                httpContext.Response.Body, body, JsonSerializerOptionsProvider.Options, httpContext.RequestAborted);
        }

        internal readonly struct MinimalResponse(HealthStatus status)
        {
            public HealthStatus Status { get; } = status;
        }
    }
}
