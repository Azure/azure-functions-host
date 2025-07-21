// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Health check related extension methods.
    /// </summary>
    internal static class HealthCheckExtensions
    {
        /// <summary>
        /// Registers all health check services required for the functions host. Should be called
        /// on the WebHost.
        /// </summary>
        /// <param name="builder">The builder to register health checks with.</param>
        /// <returns>The original builder, for call chaining.</returns>
        public static IHealthChecksBuilder AddWebJobsScriptHealthChecks(this IHealthChecksBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder
                .AddWebHostHealthCheck()
                .AddScriptHostHealthCheck();

            return builder;
        }

        /// <summary>
        /// Adds a health check for the web host lifecycle.
        /// </summary>
        /// <param name="builder">The builder to register health checks with.</param>
        /// <returns>The original builder, for call chaining.</returns>
        public static IHealthChecksBuilder AddWebHostHealthCheck(this IHealthChecksBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.AddCheck<WebHostHealthCheck>(
                "az.functions.web_host.lifecycle", tags: [HealthCheckTags.Liveness]);
            return builder;
        }

        /// <summary>
        /// Adds a health check for the script host lifecycle.
        /// </summary>
        /// <param name="builder">The builder to register health checks with.</param>
        /// <returns>The original builder, for call chaining.</returns>
        public static IHealthChecksBuilder AddScriptHostHealthCheck(this IHealthChecksBuilder builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.AddCheck<ScriptHostHealthCheck>(
                "az.functions.script_host.lifecycle", tags: [HealthCheckTags.Readiness]);
            return builder;
        }
    }
}
