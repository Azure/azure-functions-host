// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Primitives;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Diagnostics.HealthChecks
{
    public class HealthCheckResponseWriter
    {
        private static readonly JsonSerializerOptions _options = CreateJsonOptions();

        public static Task WriteResponseAsync(HttpContext httpContext, HealthReport report)
        {
            ArgumentNullException.ThrowIfNull(httpContext);
            ArgumentNullException.ThrowIfNull(report);

            // We will write a detailed report if ?expand=true is present.
            if (httpContext.Request.Query.TryGetValue("expand", out StringValues value)
                && bool.TryParse(value, out bool expand) && expand)
            {
                return UIResponseWriter.WriteHealthCheckUIResponse(httpContext, report);
            }

            return WriteMinimalResponseAsync(httpContext, report);
        }

        private static Task WriteMinimalResponseAsync(HttpContext httpContext, HealthReport report)
        {
            MinimalResponse body = new(report.Status);
            return JsonSerializer.SerializeAsync(
                httpContext.Response.Body, body, _options, httpContext.RequestAborted);
        }

        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            options.Converters.Add(new JsonStringEnumConverter());

            return options;
        }

        internal readonly struct MinimalResponse(HealthStatus status)
        {
            public HealthStatus Status { get; } = status;
        }
    }
}
