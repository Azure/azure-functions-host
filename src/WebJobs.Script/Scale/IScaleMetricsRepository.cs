// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs.Host.Scale;

namespace Microsoft.Azure.WebJobs.Script.Scale
{
    /// <summary>
    /// Interface defining methods for reading/writing scale metrics to a persistent store.
    /// </summary>
    public interface IScaleMetricsRepository
    {
        /// <summary>
        /// Persist the metrics for each monitor.
        /// </summary>
        /// <param name="monitorMetrics">The collection of metrics for each monitor.</param>
        /// <returns>A task.</returns>
        Task WriteMetricsAsync(IDictionary<IScaleMonitor, ScaleMetrics> monitorMetrics);

        /// <summary>
        /// Read the metrics.
        /// </summary>
        /// <param name="monitors">The current collection of monitors.</param>
        /// <returns>Map of metrics per monitor.</returns>
        Task<IDictionary<IScaleMonitor, IList<ScaleMetrics>>> ReadMetricsAsync(IEnumerable<IScaleMonitor> monitors);
    }
}
