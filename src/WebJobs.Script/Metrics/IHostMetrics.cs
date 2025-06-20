// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the MIT License. See License.txt in the project root for license information.

namespace Microsoft.Azure.WebJobs.Script.Metrics
{
    /// <summary>
    /// Defines the host metric methods that are used to track host-level metrics.
    /// </summary>
    public interface IHostMetrics
    {
        /// <summary>
        /// Increments the app failure count. This is used to track the number of times the host has failed to start
        /// due to issues where customer action is required to resolve.
        /// </summary>
        public void AppFailure();

        /// <summary>
        /// Increments the started invocation count. This is used to track the total number of function invocations
        /// that have started on a given instance.
        /// </summary>
        public void IncrementStartedInvocationCount();

        /// <summary>
        /// Measures the duration of the function’s logic execution.
        /// </summary>
        /// <param name="duration">Invoke duration.</param>
        /// <param name="functionName">Function name.</param>
        void TrackFunctionExecutionDuration(double duration, string functionName);
    }
}
