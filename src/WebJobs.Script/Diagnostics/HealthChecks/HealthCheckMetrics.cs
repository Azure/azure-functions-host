// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Azure.WebJobs.Script.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;

#nullable enable

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Contains metrics for health checks.
    /// </summary>
    /// <remarks>
    /// Code taken from: https://github.com/dotnet/extensions/blob/d32357716a5261509bf7527101b21cb6f94a0f89/src/Libraries/Microsoft.Extensions.Diagnostics.HealthChecks.Common/HealthCheckMetrics.cs.
    /// </remarks>
    public sealed partial class HealthCheckMetrics
    {
        public HealthCheckMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

#pragma warning disable CA2000 // Dispose objects before losing scope
            // We don't dispose the meter because IMeterFactory handles that
            // An issue on analyzer side: https://github.com/dotnet/roslyn-analyzers/issues/6912
            // Related documentation: https://github.com/dotnet/docs/pull/37170
            Meter meter = meterFactory.Create(HostMetrics.FaasMeterName, HostMetrics.FaasMeterVersion);
#pragma warning restore CA2000 // Dispose objects before losing scope

            HealthCheckReport = new(meter);
            UnhealthyHealthCheck = new(meter);
        }

        /// <summary>
        /// Gets the health check report histogram.
        /// </summary>
        public HealthCheckReportHistogram HealthCheckReport { get; }

        /// <summary>
        /// Gets the unhealthy health check histogram.
        /// </summary>
        public UnhealthyHealthCheckHistogram UnhealthyHealthCheck { get; }

        private static KeyValuePair<string, object?> Tag<T>(string key, T? value)
            => KeyValuePair.Create<string, object?>(key, value);

        private static double ToMetricValue(HealthStatus status)
            => status switch
            {
                HealthStatus.Unhealthy => 0,
                HealthStatus.Degraded => 0.5,
                HealthStatus.Healthy => 1,
                _ => throw new NotSupportedException($"Unexpected HealthStatus value: {status}"),
            };

        public static class Constants
        {
            private const string Prefix = "azure.functions.";
            public const string ReportMetricName = Prefix + "health_check.reports";
            public const string UnhealthyMetricName = Prefix + "health_check.unhealthy_checks";
            public const string HealthCheckTagTag = Prefix + "health_check.tag"; // Yes, tag tag. A metric tag with 'tag' in the name.
            public const string HealthCheckNameTag = Prefix + "health_check.name";
        }

        public class HealthCheckReportHistogram(Meter meter)
        {
            private readonly Histogram<double> _histogram
                = meter.CreateHistogram<double>(Constants.ReportMetricName);

            public void Record(HealthReport report)
                => _histogram.Record(ToMetricValue(report.Status));

            public void Record(HealthReport report, string tag)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    Record(report);
                    return;
                }

                _histogram.Record(ToMetricValue(report.Status), Tag(Constants.HealthCheckTagTag, tag));
            }
        }

        public class UnhealthyHealthCheckHistogram(Meter meter)
        {
            private readonly Histogram<double> _histogram
                = meter.CreateHistogram<double>(Constants.UnhealthyMetricName);

            public void Record(string name, HealthReportEntry entry)
                => _histogram.Record(ToMetricValue(entry.Status), Tag(Constants.HealthCheckNameTag, name));

            public void Record(string name, HealthReportEntry entry, string tag)
            {
                if (string.IsNullOrWhiteSpace(tag))
                {
                    Record(name, entry);
                    return;
                }

                _histogram.Record(
                    ToMetricValue(entry.Status),
                    Tag(Constants.HealthCheckNameTag, name),
                    Tag(Constants.HealthCheckTagTag, tag));
            }
        }
    }
}
