// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using Microsoft.Azure.WebJobs.Script.Metrics;

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Options for <see cref="ApplicationInsightsMetricExporter"/>.
    /// </summary>
    public class ApplicationInsightsMetricExporterOptions
    {
        // AppInsights has some metrics which are already emitted a different way.
        // We ignore them here to ensure we don't duplicate the metric.
        private static readonly HashSet<string> IgnoredInstruments =
        [
            HostMetrics.FaasInvokeDuration,
        ];

        /// <summary>
        /// Gets the set of meter names to listen to.
        /// </summary>
        public ISet<string> Meters { get; } = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Gets or sets the interval to collect meter values. Default is 30 seconds.
        /// </summary>
        /// <remarks>
        /// This is the interval at which values for metrics will be tracked on the Application Insights SDK. This is
        /// NOT the export interval. Application Insights SDK will export tracked values based on its own internal
        /// schedule.
        /// </remarks>
        public TimeSpan CollectInterval { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Determines if given instrument should be listened to.
        /// </summary>
        /// <param name="instrument">The instrument.</param>
        /// <returns><c>true</c> if should be listened to, <c>false</c> otherwise.</returns>
        public bool ShouldListenTo(Instrument instrument)
        {
            ArgumentNullException.ThrowIfNull(instrument);

            // TODO: consider allowing wildcards or regex
            // For now, just exact match on meter name
            return Meters.Contains(instrument.Meter.Name) && !IgnoredInstruments.Contains(instrument.Name);
        }
    }
}