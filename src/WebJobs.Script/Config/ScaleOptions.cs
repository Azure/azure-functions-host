// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Hosting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Azure.WebJobs.Script
{
    /// <summary>
    /// Defines configuration options for runtime scale monitoring.
    /// </summary>
    public class ScaleOptions : IOptionsFormatter
    {
        private TimeSpan _scaleMetricsMaxAge;
        private TimeSpan _scaleMetricsSampleInterval;

        public ScaleOptions()
        {
            // At the default values, a single monitor will be generating 6 samples per minute
            // so at 2 minutes that's 12 samples
            // Assume a case of 100 functions in an app, each mapping to a monitor. Thats
            // 1200 samples to read from storage on each scale status request.
            ScaleMetricsMaxAge = TimeSpan.FromMinutes(2);
            ScaleMetricsSampleInterval = TimeSpan.FromSeconds(10);
            MetricsPurgeEnabled = true;
        }

        // for testing, to allow us to bypass validations
        internal ScaleOptions(TimeSpan metricsSampleInterval) : this()
        {
            _scaleMetricsSampleInterval = metricsSampleInterval;
        }

        /// <summary>
        /// Gets or sets a value indicating the maximum age for metrics.
        /// Metrics that exceed this age will not be returned to monitors.
        /// </summary>
        public TimeSpan ScaleMetricsMaxAge
        {
            get
            {
                return _scaleMetricsMaxAge;
            }

            set
            {
                if (value < TimeSpan.FromMinutes(1) || value > TimeSpan.FromMinutes(5))
                {
                    throw new ArgumentOutOfRangeException(nameof(ScaleMetricsMaxAge));
                }
                _scaleMetricsMaxAge = value;
            }
        }

        /// <summary>
        /// Gets or sets the sampling interval for metrics.
        /// </summary>
        public TimeSpan ScaleMetricsSampleInterval
        {
            get
            {
                return _scaleMetricsSampleInterval;
            }

            set
            {
                if (value < TimeSpan.FromSeconds(1) || value > TimeSpan.FromSeconds(30))
                {
                    throw new ArgumentOutOfRangeException(nameof(ScaleMetricsSampleInterval));
                }
                _scaleMetricsSampleInterval = value;
            }
        }

        /// <summary>
        /// Gets or sets a value indicating whether old metrics data
        /// will be auto purged.
        /// </summary>
        public bool MetricsPurgeEnabled { get; set; }

        public string Format()
        {
            var options = new JObject
            {
                { nameof(ScaleMetricsMaxAge), ScaleMetricsMaxAge },
                { nameof(ScaleMetricsSampleInterval), ScaleMetricsSampleInterval }
            };

            return options.ToString(Formatting.Indented);
        }
    }
}
