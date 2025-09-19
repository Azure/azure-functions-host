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
        /// Registers the telemetry health check publisher with the specified additional tags.
        /// NOTE: this is currently not safe to call multiple times.
        /// </summary>
        /// <param name="builder">The builder to register to.</param>
        /// <param name="additionalTags">Registers addition copies of the publisher for these tags.</param>
        /// <returns>The original health check builder, for call chaining.</returns>
        public static IHealthChecksBuilder AddTelemetryPublisher(
            this IHealthChecksBuilder builder, params string[] additionalTags)
        {
            ArgumentNullException.ThrowIfNull(builder);

            static void RegisterPublisher(IServiceCollection services, string tag)
            {
                services.AddSingleton<IHealthCheckPublisher>(sp =>
                {
                    TelemetryHealthCheckPublisherOptions options = new() { Tag = tag };
                    return ActivatorUtilities.CreateInstance<TelemetryHealthCheckPublisher>(sp, options);
                });
            }

            builder.Services.AddLogging();
            builder.Services.AddMetrics();
            builder.Services.AddSingleton<HealthCheckMetrics>();
            RegisterPublisher(builder.Services, null); // always register the default publisher

            additionalTags ??= [];
            foreach (string tag in additionalTags.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrEmpty(tag))
                {
                    RegisterPublisher(builder.Services, tag);
                }
            }

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
                HealthCheckNames.WebHostLifeCycle, tags: [HealthCheckTags.Liveness]);
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
                HealthCheckNames.ScriptHostLifeCycle, tags: [HealthCheckTags.Readiness]);
            return builder;
        }

        /// <summary>
        /// Filters a health report to include only specified entries.
        /// </summary>
        /// <param name="result">The result to filter.</param>
        /// <param name="filter">The filter predicate to use.</param>
        /// <returns>The filtered health report.</returns>
        public static HealthReport Filter(this HealthReport result, Func<string, HealthReportEntry, bool> filter)
        {
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(filter);

            IReadOnlyDictionary<string, HealthReportEntry> newEntries = result.Entries
                .Where(x => filter(x.Key, x.Value))
                .ToDictionary();

            return new HealthReport(newEntries, result.TotalDuration);
        }
    }
}
