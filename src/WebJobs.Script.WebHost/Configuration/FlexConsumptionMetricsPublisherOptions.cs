// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System;
using Microsoft.Azure.WebJobs.Script.WebHost.Metrics;

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    /// <summary>
    /// Configuration options for <see cref="FlexConsumptionMetricsPublisher"/>.
    /// </summary>
    public class FlexConsumptionMetricsPublisherOptions
    {
        internal const int DefaultMetricsPublishIntervalMS = 5000;
        internal const int DefaultMinimumActivityIntervalMS = 100;

        public FlexConsumptionMetricsPublisherOptions()
        {
            MetricsPublishIntervalMS = DefaultMetricsPublishIntervalMS;
            MinimumActivityIntervalMS = DefaultMinimumActivityIntervalMS;
            InitialPublishDelayMS = Utility.ColdStartDelayMS;

            // Default this to 15 minutes worth of files
            MaxFileCount = 15 * (int)Math.Ceiling(1.0 * 60 / (MetricsPublishIntervalMS / 1000));
        }

        /// <summary>
        /// Gets or sets the interval at which metrics are published.
        /// </summary>
        public int MetricsPublishIntervalMS { get; set; }

        /// <summary>
        /// Gets or sets the initial delay before publishing metrics.
        /// </summary>
        /// <remarks>
        /// This has cold-start implications. We want to ensure the first publish is done after
        /// cold-start is complete.
        /// </remarks>
        public int InitialPublishDelayMS { get; set; }

        /// <summary>
        /// Gets or sets the minimum activity metering interval.
        /// </summary>
        /// <remarks>
        /// If the activity duration for an interval is less than this value, we
        /// round up.
        /// </remarks>
        public int MinimumActivityIntervalMS { get; set; }

        /// <summary>
        /// Gets or sets the file location where metrics files are published.
        /// </summary>
        public string MetricsFilePath { get; set; }

        /// <summary>
        /// Gets or sets thee maximum number of files to keep in the metrics directory.
        /// </summary>
        /// <remarks>
        /// When over this limit, the oldest files will be deleted to make room for new files.
        /// </remarks>
        public int MaxFileCount { get; set; }
    }
}
