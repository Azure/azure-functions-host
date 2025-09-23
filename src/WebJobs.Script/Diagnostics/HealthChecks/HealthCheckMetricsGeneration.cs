// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.Metrics;
using static Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks.HealthCheckMetrics;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics.HealthChecks
{
    /// <summary>
    /// Health check metrics generation and extensions.
    /// </summary>
    /// <remarks>
    /// Code adapted from: https://github.com/dotnet/extensions/blob/d32357716a5261509bf7527101b21cb6f94a0f89/src/Libraries/Microsoft.Extensions.Diagnostics.HealthChecks.Common/Metric.cs.
    /// </remarks>
    public static partial class HealthCheckMetricsGeneration
    {
        [Histogram<double>(Constants.HealthCheckTagTag, Name = Constants.ReportMetricName)]
        public static partial HealthCheckReportHistogram CreateHealthCheckReportHistogram(Meter meter);

        [Histogram<double>(
            Constants.HealthCheckNameTag, Constants.HealthCheckTagTag,
            Name = Constants.UnhealthyMetricName)]
        public static partial UnhealthyHealthCheckHistogram CreateUnhealthyHealthCheckHistogram(Meter meter);

        public static void Record(this HealthCheckReportHistogram histogram, HealthReport report, string tag)
        {
            ArgumentNullException.ThrowIfNull(report);
            histogram.Record(ToMetricValue(report.Status), tag);
        }

        public static void Record(this UnhealthyHealthCheckHistogram histogram, string name, HealthReportEntry entry, string tag)
            => histogram.Record(ToMetricValue(entry.Status), name, tag);

        private static double ToMetricValue(HealthStatus status)
            => status switch
            {
                HealthStatus.Unhealthy => 0,
                HealthStatus.Degraded => 0.5,
                HealthStatus.Healthy => 1,
                _ => throw new NotSupportedException($"Unexpected HealthStatus value: {status}"),
            };
    }
}
