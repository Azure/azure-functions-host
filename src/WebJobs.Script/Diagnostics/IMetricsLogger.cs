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
        /// <param name="eventName">The event name.</param>
        /// <param name="functionName">Optional function name for function specific events.</param>
        /// <returns>A handle to the event that was started.</returns>
        object BeginEvent(string eventName, string functionName = null, string data = null);

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
        /// Completes a previously started event.
        /// </summary>
        /// <param name="eventHandle">A previously started event.</param>
        void EndEvent(object eventHandle);

        /// <summary>
        /// Raises an event.
        /// </summary>
        /// <param name="metricEvent">The event.</param>
        void LogEvent(MetricEvent metricEvent);

        /// <summary>
        /// Raises an event.
        /// </summary>
        /// <param name="eventName">The event name.</param>
        /// <param name="functionName">Optional function name for function specific events.</param>
        void LogEvent(string eventName, string functionName = null, string data = null);
    }
}
