// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Diagnostics
{
    /// <summary>
    /// Defines an interface for emitting metric events from the 
    /// script runtime for later aggregation and reporting.
    /// </summary>
    public interface IMetricsLogger
    {
        /// <summary>
        /// Begins an event.
        /// </summary>
        /// <param name="metricEvent">The event.</param>
        void BeginEvent(MetricEvent metricEvent);

        /// <summary>
        /// Completes a previously started event.
        /// </summary>
        /// <param name="metricEvent">A previously started event.</param>
        void EndEvent(MetricEvent metricEvent);

        /// <summary>
        /// Singleton event that gets called when Host is started
        /// </summary>
        /// <param name="metricEvent">Script Host instance</param>
        void HostStartedEvent(MetricEvent metricEvent);
    }
}
