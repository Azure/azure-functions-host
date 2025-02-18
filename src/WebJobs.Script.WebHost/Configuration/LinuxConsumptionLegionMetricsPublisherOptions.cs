// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.WebHost.Configuration
{
    public class LinuxConsumptionLegionMetricsPublisherOptions
    {
        internal const int DefaultMetricsPublishIntervalMS = 30 * 1000;

        public LinuxConsumptionLegionMetricsPublisherOptions()
        {
            MetricsPublishIntervalMS = DefaultMetricsPublishIntervalMS;
        }

        public int MetricsPublishIntervalMS { get; set; }

        public string ContainerName { get; set; }

        public string MetricsFilePath { get; set; }
    }
}
